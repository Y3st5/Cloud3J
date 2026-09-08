using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Cloud2026.Core;

namespace Cloud2026.Services
{
    /// <summary>
    /// Servicio puente que conecta el gameplay con la lógica de Cloud Code.
    /// </summary>
    public class CombatService : MonoBehaviour
    {
        public static CombatService Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Envía la secuencia de movimientos al servidor.
        /// </summary>
        public async Task<CombatResponse> SubmitTurnAsync(string matchId, List<CombatClass> moves)
        {
            var cloudCode = GameBootstrap.Instance.CloudCodeService;

            // Preparamos los argumentos para el script de JS
            var args = new Dictionary<string, object>
            {
                { "matchId", matchId },
                { "moves", moves }
            };

            try
            {
                // Llamamos al módulo "CombatLogic" y a la función "submitTurn"
                // Especificamos <CombatResponse> para que el SDK deserialice la respuesta automáticamente
                var result = await cloudCode.CallModuleAsync<CombatResponse>("CombatLogic", "submitTurn", args);

                return result ?? new CombatResponse { Status = "Error", Message = "El servidor devolvió una respuesta vacía." };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CombatService] Error al enviar turno: {ex.Message}");
                return new CombatResponse { Status = "Error", Message = ex.Message };
            }
        }

        // Eliminamos ParseResponse ya que CallModuleAsync<T> hace la deserialización por nosotros.
    }

    [Serializable]
    public class CombatResponse
    {
        public string Status; // "TurnSubmitted", "RoundResolved", "Error"
        public string Message;
        public RoundResult Result; // Solo si status es RoundResolved
        public MatchState NewState;
    }
}
