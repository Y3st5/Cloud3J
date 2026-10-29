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
    /// Cliente del módulo TurnMatch.
    ///
    /// Además de llamar al servidor, este servicio gestiona el ciclo de vida de los
    /// identificadores de petición, que es donde vive la idempotencia del lado del
    /// cliente. La regla: un identificador por *jugada*, no por *envío*. Mientras no
    /// sepamos el desenlace de una jugada, cualquier reintento reutiliza el suyo.
    /// </summary>
    public class UGSTurnMatchService : MonoBehaviour, ITurnMatchService
    {
        public const string ModuleName = "TurnMatch";

        public event Action<string> OnCallFailed;

        public bool IsReady =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        public string CurrentMatchCode => _currentMatchCode;

        public bool HasPendingTurn => !string.IsNullOrEmpty(_pendingTurnRequestId);

        private string _currentMatchCode = string.Empty;

        // Jugada enviada cuyo desenlace todavía no conocemos.
        private string _pendingTurnRequestId = string.Empty;
        private int _pendingTurnExpectedNumber;

        // Última jugada con desenlace conocido, para poder reenviarla en la demo.
        private string _lastTurnRequestId = string.Empty;
        private int _lastTurnExpectedNumber;

        private string _pendingCreateRequestId = string.Empty;
        private string _pendingJoinRequestId = string.Empty;

        private bool _isCalling;

        public async Task<MatchViewDto> CreateMatchAsync()
        {
            // El mismo identificador mientras no sepamos si la partida se creó:
            // el servidor deriva el código de él, así que reintentar cae sobre la
            // misma partida en vez de crear una segunda.
            if (string.IsNullOrEmpty(_pendingCreateRequestId))
            {
                _pendingCreateRequestId = NewRequestId();
            }

            var view = await CallAsync("CreateMatch", new Dictionary<string, object>
            {
                { "requestId", _pendingCreateRequestId }
            });

            if (view != null)
            {
                _pendingCreateRequestId = string.Empty;
                _currentMatchCode = view.MatchCode;
                ResetTurnTracking();
            }

            return view;
        }

        public async Task<MatchViewDto> JoinMatchAsync(string matchCode)
        {
            var code = (matchCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                OnCallFailed?.Invoke("Escribe el código de la partida.");
                return null;
            }

            if (string.IsNullOrEmpty(_pendingJoinRequestId))
            {
                _pendingJoinRequestId = NewRequestId();
            }

            var view = await CallAsync("JoinMatch", new Dictionary<string, object>
            {
                { "matchCode", code },
                { "requestId", _pendingJoinRequestId }
            });

            if (view != null)
            {
                _pendingJoinRequestId = string.Empty;
                _currentMatchCode = view.MatchCode;
                ResetTurnTracking();
            }

            return view;
        }

        public async Task<MatchViewDto> SubmitTurnAsync(int expectedTurnNumber)
        {
            if (string.IsNullOrEmpty(_currentMatchCode))
            {
                OnCallFailed?.Invoke("No estás en ninguna partida.");
                return null;
            }

            // Si quedó una jugada sin confirmar, ésta no es una jugada nueva:
            // es el mismo intento otra vez. Mismo identificador, mismo turno.
            if (string.IsNullOrEmpty(_pendingTurnRequestId))
            {
                _pendingTurnRequestId = NewRequestId();
                _pendingTurnExpectedNumber = expectedTurnNumber;
            }

            var view = await CallAsync("SubmitTurn", new Dictionary<string, object>
            {
                { "matchCode", _currentMatchCode },
                { "requestId", _pendingTurnRequestId },
                { "expectedTurnNumber", _pendingTurnExpectedNumber }
            });

            if (view == null)
            {
                // Sin respuesta no sabemos si llegó. Conservamos el identificador
                // para que el siguiente intento sea el mismo y no cuente dos veces.
                Debug.LogWarning($"[UGSTurnMatchService] Jugada sin confirmar; " +
                                 $"se reintentará con el mismo identificador {_pendingTurnRequestId}.");
                return null;
            }

            if (view.Outcome == MatchOutcome.Conflict)
            {
                // El servidor sí respondió, pero perdió la carrera contra otra
                // escritura. La jugada no se aplicó y reintentarla es seguro.
                return view;
            }

            _lastTurnRequestId = _pendingTurnRequestId;
            _lastTurnExpectedNumber = _pendingTurnExpectedNumber;
            _pendingTurnRequestId = string.Empty;

            return view;
        }

        public async Task<MatchViewDto> ResendLastTurnAsync()
        {
            var requestId = HasPendingTurn ? _pendingTurnRequestId : _lastTurnRequestId;
            var expected = HasPendingTurn ? _pendingTurnExpectedNumber : _lastTurnExpectedNumber;

            if (string.IsNullOrEmpty(requestId))
            {
                OnCallFailed?.Invoke("Todavía no has hecho ninguna jugada que reenviar.");
                return null;
            }

            Debug.Log($"[UGSTurnMatchService] Reenviando a propósito la petición {requestId} " +
                      $"(turno esperado {expected}). El servidor debería responder 'replayed'.");

            return await CallAsync("SubmitTurn", new Dictionary<string, object>
            {
                { "matchCode", _currentMatchCode },
                { "requestId", requestId },
                { "expectedTurnNumber", expected }
            });
        }

        public Task<MatchViewDto> RefreshAsync()
        {
            if (string.IsNullOrEmpty(_currentMatchCode))
            {
                return Task.FromResult<MatchViewDto>(null);
            }

            return CallAsync("GetMatch", new Dictionary<string, object>
            {
                { "matchCode", _currentMatchCode }
            });
        }

        public void LeaveMatch()
        {
            _currentMatchCode = string.Empty;
            _pendingCreateRequestId = string.Empty;
            _pendingJoinRequestId = string.Empty;
            ResetTurnTracking();
        }

        private void ResetTurnTracking()
        {
            _pendingTurnRequestId = string.Empty;
            _pendingTurnExpectedNumber = 0;
            _lastTurnRequestId = string.Empty;
            _lastTurnExpectedNumber = 0;
        }

        /// <summary>
        /// Un identificador por jugada. No lleva información: sólo tiene que ser
        /// distinto de los demás y estable entre reintentos.
        /// </summary>
        private static string NewRequestId() => Guid.NewGuid().ToString("N");

        private async Task<MatchViewDto> CallAsync(string function, Dictionary<string, object> args)
        {
            if (!IsReady)
            {
                OnCallFailed?.Invoke("Necesitas iniciar sesión antes de jugar.");
                return null;
            }

            if (_isCalling)
            {
                Debug.LogWarning("[UGSTurnMatchService] Ya hay una llamada en curso; se ignora esta.");
                return null;
            }

            _isCalling = true;

            try
            {
                return await CloudCodeSdk.Instance.CallModuleEndpointAsync<MatchViewDto>(
                    ModuleName, function, args);
            }
            catch (CloudCodeRateLimitedException rateEx)
            {
                Report($"Demasiadas llamadas seguidas. Reinténtalo en {rateEx.RetryAfter} s.", rateEx);
                return null;
            }
            catch (CloudCodeException ccEx)
            {
                Report(Translate(ccEx, function), ccEx);
                return null;
            }
            catch (RequestFailedException reqEx)
            {
                Report($"Error de conexión con UGS ({reqEx.ErrorCode}).", reqEx);
                return null;
            }
            finally
            {
                _isCalling = false;
            }
        }

        private void Report(string message, Exception exception)
        {
            Debug.LogError($"[UGSTurnMatchService] {message}\n{exception}");
            OnCallFailed?.Invoke(message);
        }

        private static string Translate(CloudCodeException exception, string function)
        {
            switch (exception.Reason)
            {
                case CloudCodeExceptionReason.NoInternetConnection:
                    return "Sin conexión a internet.";

                case CloudCodeExceptionReason.NotFound:
                    return $"El módulo '{ModuleName}' no está desplegado en este entorno.";

                case CloudCodeExceptionReason.Unauthorized:
                    return "La sesión no tiene permiso para ejecutar este módulo.";

                case CloudCodeExceptionReason.ScriptError:
                    // El módulo lanza excepciones con mensajes pensados para el
                    // jugador (código inexistente, partida llena...). El SDK los
                    // envuelve, así que mostramos el detalle tal cual.
                    return ExtractServerMessage(exception);

                case CloudCodeExceptionReason.ServiceUnavailable:
                    return "Cloud Code no está disponible ahora mismo.";

                default:
                    return $"No se pudo completar {function}: {exception.Reason}";
            }
        }

        /// <summary>
        /// El texto de la excepción trae varias líneas de contexto del SDK. Nos
        /// quedamos con la más informativa para no llenar la pantalla de ruido.
        /// </summary>
        private static string ExtractServerMessage(CloudCodeException exception)
        {
            var text = exception.Message ?? string.Empty;
            var lines = text.Split('\n');

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.Length > 0 && !line.StartsWith("at ") && line != "ScriptError")
                {
                    return line;
                }
            }

            return "El módulo falló en el servidor. Revisa los logs de Cloud Code.";
        }
    }
}
