using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Cloud2026.Core;

namespace Cloud2026.Services
{
    /// <summary>
    /// Puente entre el gameplay y el módulo "Combat" de Cloud Code.
    ///
    /// Este servicio no decide el resultado de la pelea: envía los movimientos y
    /// el requestId al servidor y pinta lo que éste devuelva. Custodia la
    /// idempotencia igual que TurnMatch: un identificador por *acción*, no por
    /// envío, reutilizado mientras su desenlace sea desconocido, para que un
    /// reintento de red no aplique dos veces la misma jugada.
    /// </summary>
    public class CombatService : MonoBehaviour
    {
        public static CombatService Instance { get; private set; }

        public const string ModuleName = "Combat";

        /// <summary>Estados que el módulo devuelve en CombatResponse.Status.</summary>
        public const string StatusTurnSubmitted = "TurnSubmitted";
        public const string StatusRoundResolved = "RoundResolved";
        public const string StatusVictoryClaimed = "VictoryClaimed";
        public const string StatusError = "Error";

        // Acciones enviadas cuyo desenlace todavía no conocemos. Mientras una siga
        // pendiente, los reintentos reutilizan su requestId (y el servidor lo ve
        // como la misma petición).
        private string _pendingSubmitRequestId = string.Empty;
        private string _pendingCreateRequestId = string.Empty;
        private string _pendingJoinRequestId = string.Empty;
        private string _pendingClaimRequestId = string.Empty;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>Crea un duelo y lo deja esperando al segundo gremio.</summary>
        public async Task<CombatResponse> CreateMatchAsync()
        {
            if (string.IsNullOrEmpty(_pendingCreateRequestId))
            {
                _pendingCreateRequestId = NewRequestId();
            }

            var response = await CallAsync<CombatResponse>("CreateMatch", new Dictionary<string, object>
            {
                { "requestId", _pendingCreateRequestId }
            });

            ReleasePendingWhenApplied(response, ref _pendingCreateRequestId);
            return response ?? Error("El servidor no respondió o falló la llamada.");
        }

        /// <summary>Se une a un duelo existente a partir de su identificador.</summary>
        public async Task<CombatResponse> JoinMatchAsync(string matchId)
        {
            var id = (matchId ?? string.Empty).Trim().ToUpperInvariant();
            if (id.Length == 0)
            {
                return Error("Escribe el identificador del duelo.");
            }

            if (string.IsNullOrEmpty(_pendingJoinRequestId))
            {
                _pendingJoinRequestId = NewRequestId();
            }

            var response = await CallAsync<CombatResponse>("JoinMatch", new Dictionary<string, object>
            {
                { "matchId", id },
                { "requestId", _pendingJoinRequestId }
            });

            ReleasePendingWhenApplied(response, ref _pendingJoinRequestId);
            return response ?? Error("El servidor no respondió o falló la llamada.");
        }

        /// <summary>
        /// Envía la secuencia de 3 movimientos al servidor. Devuelve la respuesta
        /// del módulo: "TurnSubmitted" si el asalto quedó a medias, "RoundResolved"
        /// con el resultado si la ronda se resolvió, "Error" si hubo conflicto.
        /// </summary>
        public async Task<CombatResponse> SubmitTurnAsync(string matchId, List<CombatClass> moves)
        {
            if (string.IsNullOrEmpty(_pendingSubmitRequestId))
            {
                _pendingSubmitRequestId = NewRequestId();
            }

            // Los enums viajan como texto ("Warrior", "Mage", "Assassin"): es el
            // contrato de red y no depende del orden numérico que use el cliente.
            var moveNames = (moves ?? new List<CombatClass>())
                .Select(m => m.ToString())
                .ToList();

            var response = await CallAsync<CombatResponse>("SubmitTurn", new Dictionary<string, object>
            {
                { "matchId", matchId },
                { "requestId", _pendingSubmitRequestId },
                { "moves", moveNames }
            });

            ReleasePendingWhenApplied(response, ref _pendingSubmitRequestId);
            return response ?? Error("El servidor no respondió o falló la llamada.");
        }

        /// <summary>
        /// Reclama la victoria por incomparecencia. El servidor comprueba su propio
        /// reloj: si no han pasado los 5 minutos, devuelve "Error".
        /// </summary>
        public async Task<CombatResponse> ClaimVictoryAsync(string matchId)
        {
            if (string.IsNullOrEmpty(_pendingClaimRequestId))
            {
                _pendingClaimRequestId = NewRequestId();
            }

            var response = await CallAsync<CombatResponse>("ClaimVictory", new Dictionary<string, object>
            {
                { "matchId", matchId },
                { "requestId", _pendingClaimRequestId }
            });

            ReleasePendingWhenApplied(response, ref _pendingClaimRequestId);
            return response ?? Error("El servidor no respondió o falló la llamada.");
        }

        /// <summary>Consulta el estado actual de un duelo.</summary>
        public async Task<MatchState> GetMatchAsync(string matchId)
        {
            return await CallAsync<MatchState>("GetMatch", new Dictionary<string, object>
            {
                { "matchId", matchId }
            });
        }

        /// <summary>Lista los duelos en curso del jugador.</summary>
        public async Task<List<MatchState>> GetActiveMatchesAsync()
        {
            return await CallAsync<List<MatchState>>("GetActiveMatches", new Dictionary<string, object>());
        }

        /// <summary>
        /// Si el servidor respondió con efectos (o repitió una petición conocida),
        /// la acción ya no está pendiente y un futuro reintento puede usar un
        /// requestId nuevo. Si respondió "Error" por conflicto de escritura, lo
        /// conservamos: reintentar con el mismo id es exactamente lo seguro.
        /// </summary>
        private static void ReleasePendingWhenApplied(CombatResponse response, ref string pending)
        {
            if (response != null && response.Status != StatusError)
            {
                pending = string.Empty;
            }
        }

        /// <summary>
        /// Un identificador por acción. No lleva información: sólo tiene que ser
        /// distinto de los demás y estable entre reintentos.
        /// </summary>
        private static string NewRequestId() => Guid.NewGuid().ToString("N");

        private async Task<T> CallAsync<T>(string function, Dictionary<string, object> args)
        {
            var cloudCode = CurrentCloudCodeService();
            if (cloudCode == null)
            {
                Debug.LogError($"[CombatService] No hay un servicio de Cloud Code disponible para {function}.");
                return default;
            }

            return await cloudCode.CallModuleAsync<T>(ModuleName, function, args);
        }

        /// <summary>
        /// Prefiere el servicio registrado en el bootstrap (sesión y rate limiting
        /// compartidos con el resto del juego); si no existe, lo busca en la escena
        /// para que la demo funcione igualmente.
        /// </summary>
        private static ICloudCodeService CurrentCloudCodeService()
        {
            if (GameBootstrap.Instance != null && GameBootstrap.Instance.CloudCodeService != null)
            {
                return GameBootstrap.Instance.CloudCodeService;
            }

            return FindFirstObjectByType<UGSCloudCodeService>();
        }

        private static CombatResponse Error(string message)
        {
            return new CombatResponse { Status = StatusError, Message = message };
        }
    }

    [Serializable]
    public class CombatResponse
    {
        public string Status; // "TurnSubmitted", "RoundResolved", "VictoryClaimed", "Error"
        public string Message;
        public RoundResult Result; // Solo si Status es "RoundResolved"
        public MatchState NewState; // Estado del duelo tras la operación
    }
}