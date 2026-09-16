using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cloud2026.Gameplay;
using Cloud2026.Services;
using System.Collections.Generic;

namespace Cloud2026.UI
{
    public class CombatUI : MonoBehaviour
    {
        [Header("Referencias Manager")]
        [SerializeField] private CombatManager combatManager;

        [Header("UI de Selección")]
        [SerializeField] private Button btnWarrior;
        [SerializeField] private Button btnMage;
        [SerializeField] private Button btnAssassin;
        [SerializeField] private Button btnSubmit;
        [SerializeField] private Button btnClear;

        [Header("Visualización de Slots")]
        [SerializeField] private Image[] slotImages; // 3 slots
        [SerializeField] private Sprite spriteWarrior;
        [SerializeField] private Sprite spriteMage;
        [SerializeField] private Sprite spriteAssassin;

        [Header("Feedback")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI hpTextP1;
        [SerializeField] private TextMeshProUGUI hpTextP2;
        [SerializeField] private Button btnClaimVictory; // NUEVO: Botón para reclamar victoria

        private void Start()
        {
            if (combatManager == null)
            {
                Debug.LogError("[CombatUI] CombatManager no asignado en el Inspector.");
                return;
            }

            // Suscribirse a eventos del manager
            combatManager.OnStateChanged += HandleStateChanged;
            combatManager.OnRoundResolved += HandleRoundResolved;
            combatManager.OnCombatError += (msg) =>
            {
                if (statusText != null) statusText.text = $"Error: {msg}";
            };
            combatManager.OnTimeoutStatusChanged += HandleTimeoutStatusChanged;

            // Sincronizar UI con el estado inicial (botones y textos).
            HandleStateChanged(combatManager.CurrentState);
            UpdateSlots();
        }

        // Métodos públicos cableados en el Inspector (persistent calls de los buttons).

        public void OnWarriorClicked()
        {
            SelectClass(CombatClass.Warrior);
        }

        public void OnMageClicked()
        {
            SelectClass(CombatClass.Mage);
        }

        public void OnAssassinClicked()
        {
            SelectClass(CombatClass.Assassin);
        }

        public void OnSubmitClicked()
        {
            if (combatManager != null) combatManager.SubmitMoves();
        }

        public void OnClearClicked()
        {
            if (combatManager == null) return;
            combatManager.ClearMoves();
            UpdateSlots();
        }

        public void OnClaimVictoryClicked()
        {
            if (combatManager != null) combatManager.ClaimVictory();
        }

        private void HandleTimeoutStatusChanged(bool isExpired)
        {
            if (btnClaimVictory != null)
            {
                btnClaimVictory.gameObject.SetActive(isExpired);
            }
        }

        private void SelectClass(CombatClass combatClass)
        {
            combatManager.AddMove(combatClass);
            UpdateSlots();
        }

        private void UpdateSlots()
        {
            for (int i = 0; i < slotImages.Length; i++)
            {
                if (i < combatManager.SelectedMoves.Count)
                {
                    slotImages[i].sprite = GetSpriteForClass(combatManager.SelectedMoves[i]);
                    slotImages[i].color = Color.white;
                }
                else
                {
                    slotImages[i].sprite = null;
                    slotImages[i].color = new Color(1, 1, 1, 0.2f);
                }
            }
        }

        private Sprite GetSpriteForClass(CombatClass combatClass)
        {
            return combatClass switch
            {
                CombatClass.Warrior => spriteWarrior,
                CombatClass.Mage => spriteMage,
                CombatClass.Assassin => spriteAssassin,
                _ => null
            };
        }

        private void HandleStateChanged(CombatManager.CombatState state)
        {
            if (btnSubmit != null)
            {
                btnSubmit.interactable = (state == CombatManager.CombatState.Planning);
            }

            if (statusText != null)
            {
                statusText.text = state switch
                {
                    CombatManager.CombatState.Planning => "Planifica tu secuencia...",
                    CombatManager.CombatState.Submitting => "Enviando jugada a la nube...",
                    CombatManager.CombatState.Resolving => "¡Resolviendo choque!",
                    CombatManager.CombatState.Idle => "Esperando respuesta del rival...",
                    CombatManager.CombatState.Finished => "Partida Finalizada",
                    _ => ""
                };
            }

            if (combatManager != null && combatManager.CurrentMatchState != null)
            {
                if (hpTextP1 != null) hpTextP1.text = $"HP: {combatManager.CurrentMatchState.Player1HP}";
                if (hpTextP2 != null) hpTextP2.text = $"HP: {combatManager.CurrentMatchState.Player2HP}";
            }
        }

        private void HandleRoundResolved(RoundResult result)
        {
            // Aquí se dispararían las animaciones de los 3 choques
            Debug.Log($"Ronda {result.RoundNumber} resuelta. P1 daño: {result.P1DamageTaken}, P2 daño: {result.P2DamageTaken}");

            // Feedback visual rápido
            if (statusText != null) statusText.text = "¡Ronda Resuelta!";
            UpdateSlots();
        }
    }
}
