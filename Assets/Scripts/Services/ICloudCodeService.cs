using System;
using System.Threading.Tasks;

namespace Cloud2026.Services
{
    /// <summary>
    /// Contrato para hablar con los módulos de Cloud Code. Desacopla la UI y el
    /// gameplay del SDK de UGS, igual que hace IAuthService con Authentication.
    /// </summary>
    public interface ICloudCodeService
    {
        /// <summary>Se dispara cuando una llamada falla, con un mensaje apto para la UI.</summary>
        event Action<string> OnCallFailed;

        /// <summary>
        /// True si los servicios están inicializados y hay sesión iniciada.
        /// Cloud Code exige un jugador autenticado: sin sesión no hay llamada.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Pide al módulo HelloWorld que componga un saludo.
        /// Devuelve null si la llamada falla; el motivo va al log y a OnCallFailed.
        /// </summary>
        /// <param name="displayName">Nombre a mostrar. Puramente cosmético.</param>
        Task<GreetingResult> SayHelloAsync(string displayName);
    }
}
