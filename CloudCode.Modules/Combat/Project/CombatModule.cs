using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace Combat;

/// <summary>
/// Duelo por asaltos entre dos gremios, con el servidor como única autoridad.
///
/// El combate no se decide en el cliente (regla de arquitectura del proyecto):
/// este módulo valida los movimientos, resuelve los choques, reparte el daño y
/// declara el ganador. El cliente pide, Cloud Code decide, el cliente pinta el
/// resultado.
///
/// Al igual que TurnMatch, defiende dos problemas distintos:
///   - El requestId evita que reintentar una petición la aplique dos veces.
///   - El write lock evita que dos peticiones simultáneas se pisen el estado.
/// </summary>
public class CombatModule
{
    /// <summary>Intentos de derivar un identificador libre antes de rendirse.</summary>
    private const int MaxCodeAttempts = 5;

    private readonly CombatRepository _repository;
    private readonly ILogger<CombatModule> _logger;

    public CombatModule(IGameApiClient gameApiClient, ILogger<CombatModule> logger)
    {
        _repository = new CombatRepository(gameApiClient);
        _logger = logger;
    }

    /// <summary>
    /// Crea un duelo y devuelve su identificador para que el rival se una.
    ///
    /// Es idempotente sin necesidad de recordar nada: el identificador se deriva
    /// del requestId, así que reintentar cae sobre el duelo que ya se creó.
    /// </summary>
    [CloudCodeFunction("CreateMatch")]
    public async Task<CombatResponse> CreateMatch(IExecutionContext context, string requestId)
    {
        var playerId = RequirePlayer(context);
        RequireRequestId(requestId);

        var now = DateTime.UtcNow;

        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var matchId = CombatCode.FromRequestId(requestId, attempt);
            var stored = await _repository.LoadAsync(context, matchId);

            if (stored == null)
            {
                var match = NewMatch(matchId, playerId, requestId, now);
                match.UpdatedAtUtc = now.ToString("o");

                if (!await _repository.TrySaveAsync(context, match, null))
                {
                    return Error("Otra escritura se adelantó al crear el duelo. Vuelve a intentarlo.");
                }

                _logger.LogInformation("Duelo {MatchId} creado por {PlayerId}", matchId, playerId);
                await _repository.AddToIndexAsync(context, matchId);

                return Built(match, CombatOutcome.Applied, string.Empty);
            }

            // Ya existe algo con ese identificador. Si salió de esta misma
            // petición, es que el intento anterior sí llegó: lo devolvemos tal cual.
            if (stored.State.CreatedByRequestId == requestId && stored.State.Player1Id == playerId)
            {
                _logger.LogInformation("CreateMatch reintentado sobre el duelo {MatchId}", matchId);
                return Built(stored.State, CombatOutcome.Replayed,
                    "Este duelo ya se había creado con esta misma petición.");
            }

            // Colisión con el duelo de otra persona: probamos el siguiente candidato.
        }

        throw new Exception(
            "No se ha encontrado un identificador de duelo libre. Vuelve a intentarlo con una petición nueva.");
    }

    /// <summary>
    /// Une al jugador que llama a un duelo existente y lo deja listo para jugar.
    /// </summary>
    [CloudCodeFunction("JoinMatch")]
    public async Task<CombatResponse> JoinMatch(IExecutionContext context, string matchId, string requestId)
    {
        var playerId = RequirePlayer(context);
        RequireRequestId(requestId);

        var matchIdCode = RequireMatchId(matchId);
        var now = DateTime.UtcNow;

        var stored = await LoadOrThrowAsync(context, matchIdCode);
        var match = stored.State;

        // Reintento: si ya estás dentro, unirte otra vez no cambia nada.
        if (match.Player2Id == playerId)
        {
            return Built(match, CombatOutcome.Replayed, "Ya estabas en este duelo.");
        }

        if (match.Player1Id == playerId)
        {
            throw new Exception("No puedes unirte a tu propio duelo. Pásale el identificador a otra persona.");
        }

        if (match.Player2Id.Length > 0 || match.IsResolved)
        {
            throw new Exception("Este duelo ya tiene dos jugadores.");
        }

        match.Player2Id = playerId;
        match.Status = CombatStatus.Playing;
        match.UpdatedAtUtc = now.ToString("o");

        if (!await _repository.TrySaveAsync(context, match, stored.WriteLock))
        {
            return Error("Alguien se unió justo antes que tú. Reinicia el duelo para ver cómo quedó.");
        }

        _logger.LogInformation("{PlayerId} se unió al duelo {MatchId}", playerId, matchIdCode);
        await _repository.AddToIndexAsync(context, matchIdCode);

        return Built(match, CombatOutcome.Applied, string.Empty);
    }

    /// <summary>
    /// Envía la secuencia de 3 movimientos de la ronda en curso. Si con ello los
    /// dos ya jugaron la ronda, la resuelve y aplica el daño.
    /// </summary>
    /// <param name="moves">
    /// Los movimientos llegan como texto por contrato ("Warrior", "Mage",
    /// "Assassin"); jamás se confía en el orden numérico que use el cliente.
    /// </param>
    [CloudCodeFunction("SubmitTurn")]
    public async Task<CombatResponse> SubmitTurn(
        IExecutionContext context, string matchId, string requestId, List<string>? moves)
    {
        var playerId = RequirePlayer(context);
        RequireRequestId(requestId);

        var matchIdCode = RequireMatchId(matchId);
        var now = DateTime.UtcNow;

        var stored = await LoadOrThrowAsync(context, matchIdCode);
        var match = stored.State;

        // Lo primero, antes que cualquier validación: ¿ya habíamos aplicado esto?
        // Si se comprobara después, un reintento legítimo se rechazaría porque el
        // estado ya avanzó y el jugador se quedaría sin saber si su jugada contó.
        var processed = CombatRules.FindProcessed(match, requestId);
        if (processed != null)
        {
            return Replay(processed);
        }

        if (!match.IsParticipant(playerId))
        {
            throw new Exception("No participas en este duelo.");
        }

        if (match.Status != CombatStatus.Playing || match.IsResolved)
        {
            throw new Exception("El duelo aún espera a que se una el segundo jugador.");
        }

        var parsed = new List<CombatClass>(CombatRules.MovesPerRound);
        if (moves != null)
        {
            foreach (var move in moves)
            {
                parsed.Add(CombatRules.ParseMove(move));
            }
        }

        if (!CombatRules.AreMovesValid(parsed))
        {
            throw new Exception("Debes enviar exactamente 3 movimientos válidos.");
        }

        var resolvesRound = CombatRules.RecordMoves(match, playerId, parsed);
        match.LastMoveTimestamp = new DateTimeOffset(now).ToUnixTimeMilliseconds();
        match.UpdatedAtUtc = now.ToString("o");

        CombatResponse response;
        if (resolvesRound)
        {
            var result = CombatRules.ResolveRound(match);

            CombatRules.Remember(match, new ProcessedRequest
            {
                RequestId = requestId,
                Status = CombatOutcome.RoundResolved,
                RoundApplied = result.RoundNumber,
                Result = result,
                SnapshotMatchId = match.MatchId,
                SnapshotPlayer1Id = match.Player1Id,
                SnapshotPlayer2Id = match.Player2Id,
                SnapshotPlayer1HP = match.Player1HP,
                SnapshotPlayer2HP = match.Player2HP,
                SnapshotCurrentRound = match.CurrentRound,
                SnapshotStatus = match.Status,
                SnapshotWinnerId = match.WinnerId,
                SnapshotIsResolved = match.IsResolved,
                SnapshotLastMoveTimestamp = match.LastMoveTimestamp
            });

            response = WithResult(match, result);
        }
        else
        {
            CombatRules.Remember(match, new ProcessedRequest
            {
                RequestId = requestId,
                Status = CombatOutcome.TurnSubmitted,
                RoundApplied = match.CurrentRound
            });

            response = Built(match, CombatOutcome.TurnSubmitted, string.Empty);
        }

        if (!await _repository.TrySaveAsync(context, match, stored.WriteLock))
        {
            // El estado en memoria ya está modificado pero no llegó a guardarse.
            // El requestId queda sin confirmar: reintentar es seguro y caerá en
            // "already processed" una vez se guarde el primer intento.
            return Error("El rival escribió a la vez que tú. Reintenta con la misma petición: es seguro.");
        }

        _logger.LogInformation(
            "{PlayerId} jugó {Count} movimientos en {MatchId} (ronda {Round})",
            playerId, parsed.Count, matchIdCode, match.CurrentRound);

        return response;
    }

    /// <summary>
    /// Reclama la victoria por incomparecencia cuando el rival no ha movido en
    /// suficiente tiempo. La hora la marca el servidor, nunca el cliente.
    /// </summary>
    [CloudCodeFunction("ClaimVictory")]
    public async Task<CombatResponse> ClaimVictory(IExecutionContext context, string matchId, string requestId)
    {
        var playerId = RequirePlayer(context);
        RequireRequestId(requestId);

        var matchIdCode = RequireMatchId(matchId);
        var now = DateTime.UtcNow;

        var stored = await LoadOrThrowAsync(context, matchIdCode);
        var match = stored.State;

        if (!match.IsParticipant(playerId))
        {
            throw new Exception("No participas en este duelo.");
        }

        if (match.IsResolved)
        {
            throw new Exception("Este duelo ya se resolvió.");
        }

        var processed = CombatRules.FindProcessed(match, requestId);
        if (processed != null)
        {
            return Replay(processed);
        }

        var elapsed = now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var elapsedMs = (long)elapsed.TotalMilliseconds - match.LastMoveTimestamp;
        if (elapsedMs < CombatRules.VictoryTimeoutMs)
        {
            _logger.LogInformation(
                "{PlayerId} intentó reclamar en {MatchId} con {Ms} ms sin movimientos; se rechaza",
                playerId, matchIdCode, elapsedMs);

            throw new Exception("Todavía no han pasado los 5 minutos sin movimiento. Sigue jugando.");
        }

        match.IsResolved = true;
        match.WinnerId = playerId;
        match.Status = CombatStatus.Resolved;
        match.UpdatedAtUtc = now.ToString("o");

        CombatRules.Remember(match, new ProcessedRequest
        {
            RequestId = requestId,
            Status = CombatOutcome.VictoryClaimed,
            RoundApplied = match.CurrentRound
        });

        if (!await _repository.TrySaveAsync(context, match, stored.WriteLock))
        {
            return Error("Alguien escribió a la vez que tú mientras reclamabas. Reintenta: es seguro.");
        }

        _logger.LogInformation("{PlayerId} reclamó la victoria en {MatchId} por incomparecencia",
            playerId, matchIdCode);

        return Built(match, CombatOutcome.VictoryClaimed, string.Empty);
    }

    /// <summary>
    /// Consulta el estado de un duelo concreto. Solo quien participa puede verlo.
    /// </summary>
    [CloudCodeFunction("GetMatch")]
    public async Task<CombatMatch> GetMatch(IExecutionContext context, string matchId)
    {
        var playerId = RequirePlayer(context);
        var matchIdCode = RequireMatchId(matchId);

        var stored = await LoadOrThrowAsync(context, matchIdCode);
        if (!stored.State.IsParticipant(playerId))
        {
            throw new Exception("No participas en este duelo.");
        }

        return stored.State;
    }

    /// <summary>
    /// Lista los duelos en curso del jugador. Usa el índice privado que se va
    /// rellenando al crear y al unirse, y que se guarda como custom data privada
    /// del jugador: para el jugador, el índice es su CRUD personal.
    /// </summary>
    [CloudCodeFunction("GetActiveMatches")]
    public async Task<List<CombatMatch>> GetActiveMatches(IExecutionContext context)
    {
        RequirePlayer(context);

        var index = await _repository.LoadIndexAsync(context);
        var matches = new List<CombatMatch>();

        foreach (var matchId in index.MatchIds)
        {
            try
            {
                var stored = await _repository.LoadAsync(context, matchId);
                if (stored == null || stored.State.IsResolved)
                {
                    continue;
                }

                // El índice puede quedarse obsoleto (duelo terminado, resuelto o
                // ya con dos jugadores): solo se listan duelos en curso.
                if (stored.State.Status == CombatStatus.Playing)
                {
                    matches.Add(stored.State);
                }
            }
            catch (Exception ex)
            {
                // Un duelo que no se pudo cargar no debe tumbar la lista entera.
                _logger.LogWarning("No se pudo cargar el duelo {MatchId} del índice: {Error}",
                    matchId, ex.Message);
            }
        }

        return matches;
    }

    private static CombatMatch NewMatch(string matchId, string playerId, string requestId, DateTime now)
    {
        return new CombatMatch
        {
            MatchId = matchId,
            CreatedByRequestId = requestId,
            Player1Id = playerId,
            Status = CombatStatus.WaitingForGuest,
            Player1HP = CombatRules.MaxHealth,
            Player2HP = CombatRules.MaxHealth,
            CurrentRound = 1,
            CreatedAtUtc = now.ToString("o"),
            UpdatedAtUtc = now.ToString("o")
        };
    }

    private static CombatResponse Replay(ProcessedRequest processed)
    {
        if (processed.Status == CombatOutcome.RoundResolved && processed.Result != null)
        {
            return new CombatResponse
            {
                Status = CombatOutcome.RoundResolved,
                Message = $"Esta jugada ya se había aplicado; resolvió la ronda {processed.RoundApplied}.",
                Result = processed.Result,
                NewState = CombatRules.SnapshotOf(processed)
            };
        }

        if (processed.Status == CombatOutcome.VictoryClaimed)
        {
            return new CombatResponse
            {
                Status = CombatOutcome.VictoryClaimed,
                Message = "Esta victoria ya se había reclamado con la misma petición."
            };
        }

        return new CombatResponse
        {
            Status = CombatOutcome.TurnSubmitted,
            Message = "Esta jugada ya se había enviado y sigue esperando al rival."
        };
    }

    private static CombatResponse Built(CombatMatch match, string status, string message)
    {
        return new CombatResponse
        {
            Status = status,
            Message = message,
            NewState = match
        };
    }

    private static CombatResponse WithResult(CombatMatch match, RoundResult? result)
    {
        return new CombatResponse
        {
            Status = CombatOutcome.RoundResolved,
            Result = result,
            NewState = match
        };
    }

    private static CombatResponse Error(string message)
    {
        return new CombatResponse { Status = CombatOutcome.Error, Message = message };
    }

    private async Task<StoredCombat> LoadOrThrowAsync(IExecutionContext context, string matchId)
    {
        var stored = await _repository.LoadAsync(context, matchId);
        if (stored == null)
        {
            throw new Exception($"No hay ningún duelo con el identificador {matchId}.");
        }

        return stored;
    }

    private static string RequirePlayer(IExecutionContext context)
    {
        var playerId = context.PlayerId;
        if (string.IsNullOrEmpty(playerId))
        {
            throw new Exception("Esta operación necesita una sesión de jugador iniciada.");
        }

        return playerId;
    }

    /// <summary>
    /// El requestId lo pone el cliente, así que no confiamos en su forma. Sólo
    /// exigimos que exista y que no sea absurdamente largo: su contenido nos da
    /// igual mientras el cliente lo reutilice al reintentar.
    /// </summary>
    private static void RequireRequestId(string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64)
        {
            throw new Exception("El identificador de la petición falta o no es válido.");
        }
    }

    private static string RequireMatchId(string? matchId)
    {
        if (!CombatCode.IsWellFormed(matchId))
        {
            throw new Exception("El identificador del duelo no tiene el formato esperado.");
        }

        return CombatCode.Normalize(matchId);
    }
}