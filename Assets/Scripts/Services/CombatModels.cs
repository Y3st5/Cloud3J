using System;
using System.Collections.Generic;

namespace Cloud2026.Services
{
    public enum CombatClass
    {
        Warrior,
        Mage,
        Assassin
    }

    [Serializable]
    public class PlayerTurn
    {
        public string PlayerId;
        public List<CombatClass> Moves = new List<CombatClass>();
        public long Timestamp;
    }

    [Serializable]
    public class MatchState
    {
        public string MatchId;
        public string Player1Id;
        public string Player2Id;
        public int Player1HP = 100;
        public int Player2HP = 100;
        public int CurrentRound = 1;
        public bool IsResolved = false;
        public string WinnerId = string.Empty;
        public string Status = "WaitingForGuest"; // WaitingForGuest, Playing, Resolved
        public long LastMoveTimestamp; // Unix timestamp of the last turn submitted
    }

    [Serializable]
    public class RoundResult
    {
        public int RoundNumber;
        public List<CombatCollision> Collisions = new List<CombatCollision>();
        public int P1DamageTaken;
        public int P2DamageTaken;
    }

    [Serializable]
    public class CombatCollision
    {
        public CombatClass P1Move;
        public CombatClass P2Move;
        public string Result; // "P1_WINS", "P2_WINS", "DRAW"
    }
}
