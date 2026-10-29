using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using CloudCodeSdk = Unity.Services.CloudCode.CloudCodeService;

namespace Cloud2026.Services
{
    /// <summary>
    /// Wrapper para Unity Gaming Services Cloud Code. Encapsula la llamada al SDK
    /// y traduce los fallos a mensajes que la UI pueda mostrar.
    ///
    /// Este servicio no decide nada: sólo transporta la petición al servidor y
    /// devuelve lo que el servidor responda.
    /// </summary>
    public class UGSCloudCodeService : MonoBehaviour, ICloudCodeService
    {
        /// <summary>
        /// Nombre del módulo desplegado en UGS. Lo deriva la ventana de Deployment del
        /// nombre del .csproj (CloudCode.Modules/HelloWorld/Project/HelloWorld.csproj),
        /// así que si se renombra el proyecto hay que renombrarlo también aquí.
        /// </summary>
        public const string ModuleName = "HelloWorld";

        private const string SayHelloFunction = "SayHello";

        public event Action<string> OnCallFailed;

        public bool IsReady =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        private bool _isCalling;

        /// <summary>
        /// Llama al endpoint SayHello del módulo HelloWorld.
        /// </summary>
        public async Task<GreetingResult> SayHelloAsync(string displayName)
        {
            if (!IsReady)
            {
                const string msg = "Necesitas iniciar sesión antes de llamar a Cloud Code.";
                Debug.LogWarning($"[UGSCloudCodeService] {msg}");
                OnCallFailed?.Invoke(msg);
                return null;
            }

            if (_isCalling)
            {
                Debug.LogWarning("[UGSCloudCodeService] Ya hay una llamada en curso; se ignora esta.");
                return null;
            }

            _isCalling = true;

            try
            {
                // Enviamos únicamente el nombre a mostrar. Ni el PlayerId ni la hora:
                // esos los pone el servidor a partir del token de sesión y de su reloj.
                var args = new Dictionary<string, object>
                {
                    { "displayName", displayName ?? string.Empty }
                };

                Debug.Log($"[UGSCloudCodeService] Llamando a {ModuleName}.{SayHelloFunction}...");

                var result = await CloudCodeSdk.Instance.CallModuleEndpointAsync<GreetingResult>(
                    ModuleName, SayHelloFunction, args);

                Debug.Log($"[UGSCloudCodeService] Respuesta recibida del servidor: \"{result?.Message}\"");
                return result;
            }
            catch (CloudCodeRateLimitedException rateEx)
            {
                string errorMsg = $"Demasiadas llamadas seguidas. Reinténtalo en {rateEx.RetryAfter} s.";
                Debug.LogError($"[UGSCloudCodeService] {errorMsg} ({rateEx.Reason})");
                OnCallFailed?.Invoke(errorMsg);
                return null;
            }
            catch (CloudCodeException ccEx)
            {
                string errorMsg = TranslateCloudCodeError(ccEx);
                Debug.LogError($"[UGSCloudCodeService] {errorMsg} (Reason {ccEx.Reason}): {ccEx.Message}");
                OnCallFailed?.Invoke(errorMsg);
                return null;
            }
            catch (RequestFailedException reqEx)
            {
                string errorMsg = $"Error de conexión con UGS ({reqEx.ErrorCode}): {reqEx.Message}";
                Debug.LogError($"[UGSCloudCodeService] {errorMsg}");
                OnCallFailed?.Invoke(errorMsg);
                return null;
            }
            finally
            {
                _isCalling = false;
            }
        }

        /// <summary>
        /// Traduce el motivo del fallo a un mensaje que el jugador entienda.
        /// </summary>
        private static string TranslateCloudCodeError(CloudCodeException ex)
        {
            switch (ex.Reason)
            {
                case CloudCodeExceptionReason.NoInternetConnection:
                    return "Sin conexión a internet.";

                case CloudCodeExceptionReason.NotFound:
                    return $"El módulo '{ModuleName}' no está desplegado en este entorno. " +
                           "Despliégalo desde la ventana de Deployment.";

                case CloudCodeExceptionReason.Unauthorized:
                    return "La sesión no tiene permiso para ejecutar este módulo.";

                case CloudCodeExceptionReason.ScriptError:
                    return "El módulo falló en el servidor. Revisa los logs de Cloud Code en el Dashboard.";

                case CloudCodeExceptionReason.ServiceUnavailable:
                    return "Cloud Code no está disponible ahora mismo. Inténtalo más tarde.";

                case CloudCodeExceptionReason.InvalidArgument:
                    return "Los parámetros enviados no son válidos para este endpoint.";

                default:
                    return $"No se pudo completar la llamada a Cloud Code: {ex.Reason}";
            }
        }
    }
}
