using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cloud2026.Services;
using Cloud2026.Gameplay;

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
                // Los duelos en curso viven en el módulo "Combat" de Cloud Code.
                var matches = await FindCombatService().GetActiveMatchesAsync();

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

        /// <summary>
        /// Prefiere la instancia ya cargada en la escena; si falta, la busca para
        /// que el hub funcione aunque otro objeto no la haya enfocado aún.
        /// </summary>
        private static CombatService FindCombatService()
        {
            if (CombatService.Instance != null)
            {
                return CombatService.Instance;
            }

            return FindFirstObjectByType<CombatService>();
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
