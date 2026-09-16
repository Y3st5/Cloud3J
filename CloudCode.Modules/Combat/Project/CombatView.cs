using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Combat;

/// <summary>
/// Respuesta que el cliente pinta. Los nombres y textos coinciden con los
/// modelos de Assets/Scripts/Services/CombatModels.cs: es el contrato visible.
/// </summary>
public class CombatResponse
{
    /// <summary>
    /// "TurnSubmitted", "RoundResolved" o "VictoryClaimed" si entró con efectos,
    /// "applied"/"replayed" para crear/unir, o "Error" si hubo conflicto.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Solo cuando Status es "RoundResolved".</summary>
    public RoundResult? Result { get; set; }

    /// <summary>Estado del duelo tras la operación, para que el cliente lo pinte.</summary>
    public CombatMatch? NewState { get; set; }
}

/// <summary>Resultado de una ronda, con los mismos nombres que RoundResult del cliente.</summary>
public class RoundResult
{
    public int RoundNumber { get; set; }

    /// <summary>Un choque por movimiento (3 por ronda).</summary>
    public List<CombatCollision> Collisions { get; set; } = new();

    public int P1DamageTaken { get; set; }
    public int P2DamageTaken { get; set; }
}

/// <summary>
/// Choque de un movimiento. Result usa los textos que ya conoce la UI del
/// cliente: "P1_WINS", "P2_WINS" o "DRAW".
/// </summary>
public class CombatCollision
{
    public CombatClass P1Move { get; set; }
    public CombatClass P2Move { get; set; }
    public string Result { get; set; } = string.Empty;
}

public static class CollisionResult
{
    public const string P1Wins = "P1_WINS";
    public const string P2Wins = "P2_WINS";
    public const string Draw = "DRAW";
}

/// <summary>
/// Movimiento que manda cada jugador. Se serializa por nombre ("Warrior") para
/// que el cliente no dependa del orden del enum.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum CombatClass
{
    Warrior = 0,
    Mage = 1,
    Assassin = 2,
    Unknown = -1
}

/// <summary>Estados que el cliente reconoce en CombatResponse.Status.</summary>
public static class CombatOutcome
{
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string TurnSubmitted = "TurnSubmitted";
    public const string RoundResolved = "RoundResolved";
    public const string VictoryClaimed = "VictoryClaimed";
    public const string Error = "Error";
}