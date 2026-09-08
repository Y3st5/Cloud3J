using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Cloud2026.Services;

namespace Cloud2026.Gameplay
{
    public class CombatManager : MonoBehaviour
    {
        public enum CombatState { Idle, Planning, Submitting, Resolving, Finished }
        public CombatState CurrentState { get; private set; } = CombatState.Idle;

        [Header("Datos de Partida")]
        public string CurrentMatchId;
        public MatchState CurrentMatchState;

        [Header("Jugada Actual")]
        public List<CombatClass> SelectedMoves = new List<CombatClass>();

        public event Action<CombatState> OnStateChanged;
        public event Action<RoundResult> OnRoundResolved;
        public event Action<string> OnCombatError;

        public void StartMatch(string matchId, MatchState state)
        {
            CurrentMatchId = matchId;
            CurrentMatchState = state;
            SetState(CombatState.Planning);
        }

        public async void SubmitMoves()
        {
            if (SelectedMoves.Count != 3)
            {
                OnCombatError?.Invoke("Debes seleccionar 3 movimientos antes de confirmar.");
                return;
            }

            SetState(CombatState.Submitting);

            var response = await CombatService.Instance.SubmitTurnAsync(CurrentMatchId, SelectedMoves);

            if (response.Status == "RoundResolved")
            {
                SetState(CombatState.Resolving);
                OnRoundResolved?.Invoke(response.Result);
                CurrentMatchState = response.NewState;

                // Después de la resolución, volvemos a planificar o terminamos
                if (CurrentMatchState.Status == "Resolved")
                {
                    SetState(CombatState.Finished);
                }
                else
                {
                    SelectedMoves.Clear();
                    SetState(CombatState.Planning);
                }
            }
            else if (response.Status == "Processed" || response.Status == "TurnSubmitted")
            {
                // Turno enviado, pero el rival aún no ha movido
                SetState(CombatState.Idle); // Esperando al rival
            }
            else
            {
                OnCombatError?.Invoke(response.Message);
                SetState(CombatState.Planning);
            }
        }

        public void AddMove(CombatClass move)
        {
            if (CurrentState != CombatState.Planning) return;

            if (SelectedMoves.Count < 3)
            {
                SelectedMoves.Add(move);
            }
        }

        public void ClearMoves()
        {
            SelectedMoves.Clear();
        }

        private void SetState(CombatState newState)
        {
            CurrentState = newState;
            OnStateChanged?.Invoke(newState);
            Debug.Log($"[CombatManager] Estado cambiado a: {newState}");
        }
    }
}
