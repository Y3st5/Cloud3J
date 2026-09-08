using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cloud2026.Services;
using Cloud2026.Gameplay;
using Cloud2026.Core;

namespace Cloud2026.UI
{
    public class HubManager : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private CombatManager combatManager;
        [SerializeField] private Transform matchListContainer;
        [SerializeField] private GameObject matchEntryPrefab;
        [SerializeField] private TextMeshProUGUI hubTitleText;

        private void Start()
        {
            RefreshMatches();
        }

        public async void RefreshMatches()
        {
            hubTitleText.text = "Tus Partidas Activas...";

            // Limpiar lista actual
            foreach (Transform child in matchListContainer)
            {
                Destroy(child.gameObject);
            }

            try
            {
                // Usamos el TurnMatchService para obtener partidas donde el jugador participa
                var matches = await GameBootstrap.Instance.TurnMatchService.GetActiveMatchesAsync();

                if (matches == null || matches.Count == 0)
                {
                    hubTitleText.text = "No tienes partidas activas.";
                    return;
                }

                foreach (var match in matches)
                {
                    CreateMatchEntry(match);
                }

                hubTitleText.text = "Hub Central de Gremios";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HubManager] Error al cargar partidas: {ex.Message}");
                hubTitleText.text = "Error al cargar partidas.";
            }
        }

        private void CreateMatchEntry(MatchState match)
        {
            var entry = Instantiate(matchEntryPrefab, matchListContainer);
            var text = entry.GetComponentInChildren<TextMeshProUGUI>();
            var button = entry.GetComponent<Button>();

            text.text = $"Partida: {match.MatchId} | HP: {match.Player1HP} vs {match.Player2HP}";

            button.onClick.AddListener(() => {
                combatManager.StartMatch(match.MatchId, match);
                // Aquí se podría cargar la escena de combate o activar el panel de combate
            });
        }
    }
}
