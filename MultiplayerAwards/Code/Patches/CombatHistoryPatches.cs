using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Patches;

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.DamageReceived))]
public static class DamageReceivedPatch
{
    public static void Postfix(CombatState combatState, Creature receiver, Creature? dealer,
                               DamageResult result, CardModel? cardSource)
    {
        // Track damage dealt by player
        if (dealer != null && dealer.IsPlayer && dealer.Player != null)
        {
            var stats = RunAwardsTracker.GetOrCreate(dealer.Player.NetId);
            stats.TotalDamageDealt += result.UnblockedDamage;
            stats.OverkillDamage += result.OverkillDamage;

            if (result.UnblockedDamage > stats.HighestSingleHit)
                stats.HighestSingleHit = result.UnblockedDamage;

            // Track kills
            if (result.WasTargetKilled && !receiver.IsPlayer)
                stats.MonstersKilled++;
        }

        // Track damage taken/blocked by player
        if (receiver.IsPlayer && receiver.Player != null)
        {
            var stats = RunAwardsTracker.GetOrCreate(receiver.Player.NetId);
            stats.TotalDamageTaken += result.UnblockedDamage;
            stats.TotalDamageBlocked += result.BlockedDamage;

            // Track deaths
            if (result.WasTargetKilled)
                stats.DeathCount++;
        }
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.BlockGained))]
public static class BlockGainedPatch
{
    public static void Postfix(CombatState combatState, Creature receiver, int amount,
                               ValueProp props, CardPlay? cardPlay)
    {
        if (!receiver.IsPlayer || receiver.Player == null) return;

        // Determine who generated the block
        ulong sourceNetId;
        if (cardPlay?.Card?.Owner != null)
            sourceNetId = cardPlay.Card.Owner.NetId;
        else
            sourceNetId = receiver.Player.NetId;

        var sourceStats = RunAwardsTracker.GetOrCreate(sourceNetId);
        sourceStats.TotalBlockGained += amount;

        // Track block given to others
        if (sourceNetId != receiver.Player.NetId)
            sourceStats.BlockGivenToOthers += amount;
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayFinished))]
public static class CardPlayFinishedPatch
{
    public static void Postfix(CombatState combatState, CardPlay cardPlay)
    {
        var player = cardPlay.Card?.Owner;
        if (player == null) return;

        var stats = RunAwardsTracker.GetOrCreate(player.NetId);
        stats.TotalCardsPlayed++;

        var cardType = cardPlay.Card!.Type;
        switch (cardType)
        {
            case CardType.Attack:
                stats.AttackCardsPlayed++;
                break;
            case CardType.Skill:
                stats.SkillCardsPlayed++;
                break;
            case CardType.Power:
                stats.PowerCardsPlayed++;
                break;
        }
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardExhausted))]
public static class CardExhaustedPatch
{
    public static void Postfix(CombatState combatState, CardModel card)
    {
        var player = card?.Owner;
        if (player == null) return;

        var stats = RunAwardsTracker.GetOrCreate(player.NetId);
        stats.CardsExhausted++;
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardDrawn))]
public static class CardDrawnPatch
{
    public static void Postfix(CombatState combatState, CardModel card, bool fromHandDraw)
    {
        var player = card?.Owner;
        if (player == null) return;

        var stats = RunAwardsTracker.GetOrCreate(player.NetId);
        stats.CardsDrawn++;
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.EnergySpent))]
public static class EnergySpentPatch
{
    public static void Postfix(CombatState combatState, int amount,
                               MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (player == null) return;

        var stats = RunAwardsTracker.GetOrCreate(player.NetId);
        stats.TotalEnergySpent += amount;
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.PotionUsed))]
public static class PotionUsedPatch
{
    public static void Postfix(CombatState combatState, PotionModel potion, Creature? target)
    {
        var player = potion?.Owner;
        if (player == null) return;

        var stats = RunAwardsTracker.GetOrCreate(player.NetId);
        stats.PotionsUsed++;
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.PowerReceived))]
public static class PowerReceivedPatch
{
    public static void Postfix(CombatState combatState, PowerModel power, decimal amount, Creature? applier)
    {
        if (applier == null || !applier.IsPlayer || applier.Player == null) return;

        var stats = RunAwardsTracker.GetOrCreate(applier.Player.NetId);
        stats.TotalPowersApplied++;

        // Track debuffs applied to enemies — power.Owner is the Creature the power is on
        var powerTarget = power.Owner;
        if (powerTarget != null && !powerTarget.IsPlayer && power.Type == MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff)
            stats.DebuffsAppliedToEnemies++;
    }
}
