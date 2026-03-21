namespace MultiplayerAwards.Tracking;

public class PlayerRunStats
{
    // Identity
    public ulong NetId;
    public string CharacterName = "";
    public string PlayerDisplayName = "";

    // Damage
    public int TotalDamageDealt;
    public int TotalDamageTaken;
    public int TotalDamageBlocked;
    public int HighestSingleHit;
    public int OverkillDamage;

    // Block
    public int TotalBlockGained;
    public int BlockGivenToOthers;

    // Cards
    public int TotalCardsPlayed;
    public int AttackCardsPlayed;
    public int SkillCardsPlayed;
    public int PowerCardsPlayed;
    public int CardsExhausted;
    public int CardsDrawn;

    // Kills
    public int MonstersKilled;

    // Resources
    public int TotalEnergySpent;
    public int PotionsUsed;
    public int TotalGoldAtEnd;

    // Healing
    public int TotalHealingDone;

    // Powers
    public int TotalPowersApplied;
    public int DebuffsAppliedToEnemies;

    // Combat
    public int CombatsParticipated;
    public int TurnsPlayed;
    public int DeathCount;

    // Derived stats (computed, not tracked)
    public float DamagePerCard => TotalCardsPlayed > 0 ? (float)TotalDamageDealt / TotalCardsPlayed : 0f;
    public float DamagePerEnergy => TotalEnergySpent > 0 ? (float)TotalDamageDealt / TotalEnergySpent : 0f;
}
