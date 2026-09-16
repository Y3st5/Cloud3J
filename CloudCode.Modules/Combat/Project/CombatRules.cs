using System;
using System.Collections.Generic;

namespace Combat;

/// <summary>
/// Reglas del duelo de gremios. Son la única autoridad sobre el valor de la
/// pelea: el cliente solo manda movimientos y aquí se decide el daño, la ronda
/// y el ganador. Esta clase no toca Cloud Save ni la red: por eso los tests
/// corren sin servidor.
/// </summary>
public static class CombatRules
{
    public const int MaxHealth = 100;
    public const int DamagePerHit = 10;
    public const int MovesPerRound = 3;

    /// <summary>Tiempo sin movimientos tras el cual cualquiera puede reclamar.</summary>
    public const int VictoryTimeoutMs = 5 * 60 * 1000;

    /// <summary>
    /// Historial de peticiones que se conserva por duelo. Suficiente para
    /// proteger los reintentos de una partida normal sin dejar crecer el estado.
    /// </summary>
    public const int MaxProcessedRequests = 20;

    /// <summary>
    /// Depredación: la espada corta la daga, el conjuro tritura la armadura y la
    /// daga corta el hechizo. El movimiento ganador es el que caza al otro.
    /// </summary>
    private static readonly Dictionary<CombatClass, CombatClass> PreysOn = new()
    {
        [CombatClass.Warrior] = CombatClass.Assassin,
        [CombatClass.Mage] = CombatClass.Warrior,
        [CombatClass.Assassin] = CombatClass.Mage
    };

    public static bool AreMovesValid(IReadOnlyList<CombatClass>? moves)
    {
        if (moves == null || moves.Count != MovesPerRound)
        {
            return false;
        }

        foreach (var move in moves)
        {
            if (move == CombatClass.Unknown)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Convierte lo que llegó por la red en movimientos. El SDK puede mandar los
    /// enums por nombre ("Warrior") o por valor ("0"); se aceptan los dos y
    /// cualquier otra cosa devuelve Unknown para que no pase la validación.
    /// </summary>
    public static CombatClass ParseMove(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CombatClass.Unknown;
        }

        var trimmed = raw.Trim();
        foreach (var move in KnownMoves)
        {
            if (string.Equals(move.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return move;
            }
        }

        if (int.TryParse(trimmed, out var value) && value >= 0 && value < KnownMoves.Length)
        {
            return KnownMoves[value];
        }

        return CombatClass.Unknown;
    }

    private static readonly CombatClass[] KnownMoves =
        { CombatClass.Warrior, CombatClass.Mage, CombatClass.Assassin };

    public static bool RecordMoves(
        CombatMatch match, string playerId, IReadOnlyList<CombatClass> moves)
    {
        if (playerId == match.Player1Id)
        {
            match.Player1Moves = new List<CombatClass>(moves);
        }
        else
        {
            match.Player2Moves = new List<CombatClass>(moves);
        }

        return match.Player1Moves.Count == MovesPerRound && match.Player2Moves.Count == MovesPerRound;
    }

    /// <summary>
    /// Resuelve la ronda en curso: compara los tres choques, reparte el daño y,
    /// si alguien quedó a cero, declara al ganador. La ronda avanza y los
    /// movimientos se vacían para la siguiente.
    /// </summary>
    public static RoundResult ResolveRound(CombatMatch match)
    {
        var result = new RoundResult
        {
            RoundNumber = match.CurrentRound,
            Collisions = new List<CombatCollision>()
        };

        for (var i = 0; i < MovesPerRound; i++)
        {
            var collision = ResolveCollision(match.Player1Moves[i], match.Player2Moves[i]);
            result.Collisions.Add(collision);

            if (collision.Result == CollisionResult.P1Wins)
            {
                result.P2DamageTaken += DamagePerHit;
                match.Player2HP -= DamagePerHit;
            }
            else if (collision.Result == CollisionResult.P2Wins)
            {
                result.P1DamageTaken += DamagePerHit;
                match.Player1HP -= DamagePerHit;
            }
        }

        if (match.Player1HP <= 0 || match.Player2HP <= 0)
        {
            match.IsResolved = true;
            match.Status = CombatStatus.Resolved;
            match.WinnerId = match.Player1HP <= 0 ? match.Player2Id : match.Player1Id;
        }

        match.CurrentRound += 1;
        match.Player1Moves = new List<CombatClass>();
        match.Player2Moves = new List<CombatClass>();

        return result;
    }

    public static CombatCollision ResolveCollision(CombatClass p1, CombatClass p2)
    {
        var collision = new CombatCollision { P1Move = p1, P2Move = p2 };

        if (p1 == p2)
        {
            collision.Result = CollisionResult.Draw;
        }
        else if (PreysOn.TryGetValue(p1, out var prey) && prey == p2)
        {
            collision.Result = CollisionResult.P1Wins;
        }
        else
        {
            collision.Result = CollisionResult.P2Wins;
        }

        return collision;
    }

    // --- Idempotencia: peticiones ya aplicadas ------------------------------

    public static ProcessedRequest? FindProcessed(CombatMatch match, string requestId)
    {
        foreach (var processed in match.ProcessedRequests)
        {
            if (processed.RequestId == requestId)
            {
                return processed;
            }
        }

        return null;
    }

    /// <summary>
    /// Anota la petición aplicada y limita el historial para que no crezca sin
    /// límite en duelos largos.
    /// </summary>
    public static void Remember(CombatMatch match, ProcessedRequest processed)
    {
        match.ProcessedRequests.Add(processed);

        if (match.ProcessedRequests.Count > MaxProcessedRequests)
        {
            match.ProcessedRequests.RemoveRange(0, match.ProcessedRequests.Count - MaxProcessedRequests);
        }
    }

    /// <summary>
    /// Reconstruye el duelo tal como estaba cuando se aplicó la ronda. Es un
    /// estado nuevo, no una referencia: el que recibe puede mutarlo sin romper
    /// el guardado real.
    /// </summary>
    public static CombatMatch SnapshotOf(ProcessedRequest processed)
    {
        return new CombatMatch
        {
            MatchId = processed.SnapshotMatchId,
            Player1Id = processed.SnapshotPlayer1Id,
            Player2Id = processed.SnapshotPlayer2Id,
            Player1HP = processed.SnapshotPlayer1HP,
            Player2HP = processed.SnapshotPlayer2HP,
            CurrentRound = processed.SnapshotCurrentRound,
            Status = processed.SnapshotStatus,
            WinnerId = processed.SnapshotWinnerId,
            IsResolved = processed.SnapshotIsResolved,
            LastMoveTimestamp = processed.SnapshotLastMoveTimestamp
        };
    }
}