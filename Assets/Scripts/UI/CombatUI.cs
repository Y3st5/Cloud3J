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

        private void Start()
        {
            // Configurar botones de clase
            btnWarrior.onClick.AddListener(() => SelectClass(CombatClass.Warrior));
            btnMage.onClick.AddListener(() => SelectClass(CombatClass.Mage));
            btnAssassin.onClick.AddListener(() => SelectClass(CombatClass.Assassin));

            btnSubmit.onClick.AddListener(() => combatManager.SubmitMoves());
            btnClear.onClick.AddListener(() => {
                combatManager.ClearMoves();
                UpdateSlots();
            });

            // Suscribirse a eventos del manager
            combatManager.OnStateChanged += HandleStateChanged;
            combatManager.OnRoundResolved += HandleRoundResolved;
            combatManager.OnCombatError += (msg) => statusText.text = $"Error: {msg}";

            UpdateSlots();
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
            btnSubmit.interactable = (state == CombatManager.CombatState.Planning);

            statusText.text = state switch
            {
                CombatManager.CombatState.Planning => "Planifica tu secuencia...",
                CombatManager.CombatState.Submitting => "Enviando jugada a la nube...",
                CombatManager.CombatState.Resolving => "¡Resolviendo choque!",
                CombatManager.CombatState.Idle => "Esperando respuesta del rival...",
                CombatManager.CombatState.Finished => "Partida Finalizada",
                _ => ""
            };

            if (combatManager.CurrentMatchState != null)
            {
                hpTextP1.text = $"HP: {combatManager.CurrentMatchState.Player1HP}";
                hpTextP2.text = $"HP: {combatManager.CurrentMatchState.Player2HP}";
            }
        }

        private void HandleRoundResolved(RoundResult result)
        {
            // Aquí se dispararían las animaciones de los 3 choques
            Debug.Log($"Ronda {result.RoundNumber} resuelta. P1 daño: {result.P1DamageTaken}, P2 daño: {result.P2DamageTaken}");

            // Feedback visual rápido
            statusText.text = "¡Ronda Resuelta!";
            UpdateSlots();
        }
    }
}
