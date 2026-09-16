using System;
using System.Threading.Tasks;

namespace Cloud2026.Services
{
    /// <summary>
    /// Contrato para hablar con el módulo TurnMatch de Cloud Code.
    ///
    /// La responsabilidad interesante de este servicio no es llamar al servidor:
    /// es **custodiar los identificadores de petición**. Una jugada tiene un
    /// requestId, y ese identificador tiene que sobrevivir a todos los reintentos
    /// de esa misma jugada. Si el cliente genera uno nuevo al reintentar, el
    /// servidor no puede saber que es la misma jugada y la aplicará dos veces.
    /// </summary>
    public interface ITurnMatchService
    {
        /// <summary>Se dispara cuando una llamada falla, con un mensaje para la UI.</summary>
        event Action<string> OnCallFailed;

        bool IsReady { get; }

        /// <summary>Código de la partida en curso, o vacío si no hay ninguna.</summary>
        string CurrentMatchCode { get; }

        /// <summary>
        /// True si hay una jugada enviada cuyo desenlace no conocemos. Mientras lo
        /// sea, reintentar es seguro: se reutilizará el mismo identificador.
        /// </summary>
        bool HasPendingTurn { get; }

        Task<MatchViewDto> CreateMatchAsync();

        Task<MatchViewDto> JoinMatchAsync(string matchCode);

        /// <summary>
        /// Pasa el turno. Si quedó una jugada sin confirmar, reutiliza su
        /// identificador en vez de crear uno nuevo.
        /// </summary>
        Task<MatchViewDto> SubmitTurnAsync(int expectedTurnNumber);

        /// <summary>
        /// Reenvía la última jugada con el mismo identificador, a propósito.
        /// Existe para poder demostrar la idempotencia en clase: el servidor
        /// responde Replayed y la partida no avanza.
        /// </summary>
        Task<MatchViewDto> ResendLastTurnAsync();

        /// <summary>Consulta el estado sin intentar cambiarlo.</summary>
        Task<MatchViewDto> RefreshAsync();

        /// <summary>Olvida la partida en curso y vuelve al inicio.</summary>
        void LeaveMatch();
    }
}
