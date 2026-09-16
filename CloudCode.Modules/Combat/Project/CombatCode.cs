using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Combat;

/// <summary>
/// Identificadores de duelo derivados del identificador de la petición.
///
/// No se sortean al azar a propósito: si el identificador saliera de un
/// generador aleatorio, un reintento de "crear duelo" produciría un duelo
/// distinto y el jugador acabaría con dos. Derivándolo del requestId, reintentar
/// cae en el mismo identificador y el servidor reconoce que ya existe.
/// </summary>
public static class CombatCode
{
    /// <summary>
    /// Sin I, O, 0 ni 1: son los que se confunden al copiarlos o dictarlos.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int Length = 4;

    /// <summary>
    /// Deriva el identificador. <paramref name="attempt"/> permite buscar el
    /// siguiente candidato cuando el identificador ya lo ocupa otro duelo.
    /// </summary>
    public static string FromRequestId(string requestId, int attempt = 0)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{requestId}#{attempt}"));

        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            // 256 es múltiplo de 32, así que el módulo no sesga el reparto.
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }

    /// <summary>Acepta lo que el jugador haya tecleado y lo deja en forma canónica.</summary>
    public static string Normalize(string? input)
    {
        return (input ?? string.Empty).Trim().ToUpperInvariant();
    }

    public static bool IsWellFormed(string? code)
    {
        var normalized = Normalize(code);
        return normalized.Length == Length && normalized.All(Alphabet.Contains);
    }
}