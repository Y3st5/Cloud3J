using System.Collections.Generic;

namespace Combat;

/// <summary>
/// Estado autoritativo de un duelo. Vive en Cloud Save como dato privado de
/// custom data (collection "combat_matches"), escrito siempre con el
/// ServiceToken del módulo: ni siquiera los dos participantes pueden tocar el
/// duelo desde su cliente.
///
/// Este documento es el contrato con el cliente: sus nombres de propiedad son
/// los que espera MatchState en Assets/Scripts/Services/CombatModels.cs. Al
/// renombrar aquí, hay que renombrar allí en el mismo cambio.
/// </summary>
public class CombatMatch
{
    public string MatchId { get; set; } = string.Empty;

    /// <summary>
    /// Petición que creó el duelo. Permite reconocer un reintento de CreateMatch:
    /// si el identificador derivado ya existe y lo creó esta misma petición, es
    /// que el intento anterior sí llegó.
    /// </summary>
    public string CreatedByRequestId { get; set; } = string.Empty;

    public string Player1Id { get; set; } = string.Empty;

    /// <summary>Vacío mientras nadie se ha unido.</summary>
    public string Player2Id { get; set; } = string.Empty;

    public string Status { get; set; } = CombatStatus.WaitingForGuest;

    public int Player1HP { get; set; } = CombatRules.MaxHealth;
    public int Player2HP { get; set; } = CombatRules.MaxHealth;

    /// <summary>Ronda en curso, empezando en 1.</summary>
    public int CurrentRound { get; set; } = 1;

    public bool IsResolved { get; set; }

    /// <summary>Vacío mientras el duelo no tenga ganador.</summary>
    public string WinnerId { get; set; } = string.Empty;

    /// <summary>Movimientos de cada jugador de la ronda en curso.</summary>
    public List<CombatClass> Player1Moves { get; set; } = new();
    public List<CombatClass> Player2Moves { get; set; } = new();

    /// <summary>
    /// Hora (milisegundos Unix) del último movimiento. Es la que da derecho a
    /// reclamar la victoria si el rival no contesta; la marca el servidor, nunca
    /// el reloj del cliente.
    /// </summary>
    public long LastMoveTimestamp { get; set; }

    public string CreatedAtUtc { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;

    /// <summary>Peticiones ya aplicadas, para que reintentar no duplique efectos.</summary>
    public List<ProcessedRequest> ProcessedRequests { get; set; } = new();

    public bool IsParticipant(string playerId)
    {
        return playerId == Player1Id || (Player2Id.Length > 0 && playerId == Player2Id);
    }
}

/// <summary>
/// Fases del duelo. El cliente compara estos valores tal cual (CombatManager
/// mira "Resolved"): son texto, no un contrato que el cliente pueda saltarse.
/// </summary>
public static class CombatStatus
{
    public const string WaitingForGuest = "WaitingForGuest";
    public const string Playing = "Playing";
    public const string Resolved = "Resolved";
}

/// <summary>
/// Rastro de una petición ya procesada. Junto con la instantánea del duelo en
/// el momento de aplicarla, permite responder a un reintento exactamente igual
/// que la primera vez, sin volver a validar contra un estado que ya avanzó.
/// </summary>
public class ProcessedRequest
{
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// "TurnSubmitted", "RoundResolved" o "VictoryClaimed": lo que se respondió
    /// la primera vez que llegó esta petición.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Ronda que quedó aplicada cuando se procesó la petición.</summary>
    public int RoundApplied { get; set; }

    /// <summary>Resultado completo de la ronda resuelta, para repetirlo fielmente.</summary>
    public RoundResult? Result { get; set; }

    // Instantánea del duelo tras aplicar la ronda. Reconstruye el estado sin
    // volver a aplicar daño sobre un estado que quizá ya cambió.
    public string SnapshotMatchId { get; set; } = string.Empty;
    public string SnapshotPlayer1Id { get; set; } = string.Empty;
    public string SnapshotPlayer2Id { get; set; } = string.Empty;
    public int SnapshotPlayer1HP { get; set; }
    public int SnapshotPlayer2HP { get; set; }
    public int SnapshotCurrentRound { get; set; }
    public string SnapshotStatus { get; set; } = string.Empty;
    public string SnapshotWinnerId { get; set; } = string.Empty;
    public bool SnapshotIsResolved { get; set; }
    public long SnapshotLastMoveTimestamp { get; set; }
}