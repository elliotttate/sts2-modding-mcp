using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Networking;

public struct PlayerStatsMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public MegaCrit.Sts2.Core.Logging.LogLevel LogLevel => MegaCrit.Sts2.Core.Logging.LogLevel.Debug;

    // Identity
    public ulong SenderNetId;
    public string CharacterName;
    public string PlayerDisplayName;

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

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(SenderNetId);
        writer.WriteString(CharacterName ?? "");
        writer.WriteString(PlayerDisplayName ?? "");

        writer.WriteInt(TotalDamageDealt);
        writer.WriteInt(TotalDamageTaken);
        writer.WriteInt(TotalDamageBlocked);
        writer.WriteInt(HighestSingleHit);
        writer.WriteInt(OverkillDamage);

        writer.WriteInt(TotalBlockGained);
        writer.WriteInt(BlockGivenToOthers);

        writer.WriteInt(TotalCardsPlayed);
        writer.WriteInt(AttackCardsPlayed);
        writer.WriteInt(SkillCardsPlayed);
        writer.WriteInt(PowerCardsPlayed);
        writer.WriteInt(CardsExhausted);
        writer.WriteInt(CardsDrawn);

        writer.WriteInt(MonstersKilled);

        writer.WriteInt(TotalEnergySpent);
        writer.WriteInt(PotionsUsed);
        writer.WriteInt(TotalGoldAtEnd);

        writer.WriteInt(TotalHealingDone);

        writer.WriteInt(TotalPowersApplied);
        writer.WriteInt(DebuffsAppliedToEnemies);

        writer.WriteInt(CombatsParticipated);
        writer.WriteInt(TurnsPlayed);
        writer.WriteInt(DeathCount);
    }

    public void Deserialize(PacketReader reader)
    {
        SenderNetId = reader.ReadULong();
        CharacterName = reader.ReadString();
        PlayerDisplayName = reader.ReadString();

        TotalDamageDealt = reader.ReadInt();
        TotalDamageTaken = reader.ReadInt();
        TotalDamageBlocked = reader.ReadInt();
        HighestSingleHit = reader.ReadInt();
        OverkillDamage = reader.ReadInt();

        TotalBlockGained = reader.ReadInt();
        BlockGivenToOthers = reader.ReadInt();

        TotalCardsPlayed = reader.ReadInt();
        AttackCardsPlayed = reader.ReadInt();
        SkillCardsPlayed = reader.ReadInt();
        PowerCardsPlayed = reader.ReadInt();
        CardsExhausted = reader.ReadInt();
        CardsDrawn = reader.ReadInt();

        MonstersKilled = reader.ReadInt();

        TotalEnergySpent = reader.ReadInt();
        PotionsUsed = reader.ReadInt();
        TotalGoldAtEnd = reader.ReadInt();

        TotalHealingDone = reader.ReadInt();

        TotalPowersApplied = reader.ReadInt();
        DebuffsAppliedToEnemies = reader.ReadInt();

        CombatsParticipated = reader.ReadInt();
        TurnsPlayed = reader.ReadInt();
        DeathCount = reader.ReadInt();
    }

    public static PlayerStatsMessage FromStats(PlayerRunStats stats)
    {
        return new PlayerStatsMessage
        {
            SenderNetId = stats.NetId,
            CharacterName = stats.CharacterName,
            PlayerDisplayName = stats.PlayerDisplayName,
            TotalDamageDealt = stats.TotalDamageDealt,
            TotalDamageTaken = stats.TotalDamageTaken,
            TotalDamageBlocked = stats.TotalDamageBlocked,
            HighestSingleHit = stats.HighestSingleHit,
            OverkillDamage = stats.OverkillDamage,
            TotalBlockGained = stats.TotalBlockGained,
            BlockGivenToOthers = stats.BlockGivenToOthers,
            TotalCardsPlayed = stats.TotalCardsPlayed,
            AttackCardsPlayed = stats.AttackCardsPlayed,
            SkillCardsPlayed = stats.SkillCardsPlayed,
            PowerCardsPlayed = stats.PowerCardsPlayed,
            CardsExhausted = stats.CardsExhausted,
            CardsDrawn = stats.CardsDrawn,
            MonstersKilled = stats.MonstersKilled,
            TotalEnergySpent = stats.TotalEnergySpent,
            PotionsUsed = stats.PotionsUsed,
            TotalGoldAtEnd = stats.TotalGoldAtEnd,
            TotalHealingDone = stats.TotalHealingDone,
            TotalPowersApplied = stats.TotalPowersApplied,
            DebuffsAppliedToEnemies = stats.DebuffsAppliedToEnemies,
            CombatsParticipated = stats.CombatsParticipated,
            TurnsPlayed = stats.TurnsPlayed,
            DeathCount = stats.DeathCount
        };
    }

    public void ApplyTo(PlayerRunStats stats)
    {
        stats.NetId = SenderNetId;
        stats.CharacterName = CharacterName;
        stats.PlayerDisplayName = PlayerDisplayName;
        stats.TotalDamageDealt = TotalDamageDealt;
        stats.TotalDamageTaken = TotalDamageTaken;
        stats.TotalDamageBlocked = TotalDamageBlocked;
        stats.HighestSingleHit = HighestSingleHit;
        stats.OverkillDamage = OverkillDamage;
        stats.TotalBlockGained = TotalBlockGained;
        stats.BlockGivenToOthers = BlockGivenToOthers;
        stats.TotalCardsPlayed = TotalCardsPlayed;
        stats.AttackCardsPlayed = AttackCardsPlayed;
        stats.SkillCardsPlayed = SkillCardsPlayed;
        stats.PowerCardsPlayed = PowerCardsPlayed;
        stats.CardsExhausted = CardsExhausted;
        stats.CardsDrawn = CardsDrawn;
        stats.MonstersKilled = MonstersKilled;
        stats.TotalEnergySpent = TotalEnergySpent;
        stats.PotionsUsed = PotionsUsed;
        stats.TotalGoldAtEnd = TotalGoldAtEnd;
        stats.TotalHealingDone = TotalHealingDone;
        stats.TotalPowersApplied = TotalPowersApplied;
        stats.DebuffsAppliedToEnemies = DebuffsAppliedToEnemies;
        stats.CombatsParticipated = CombatsParticipated;
        stats.TurnsPlayed = TurnsPlayed;
        stats.DeathCount = DeathCount;
    }
}
