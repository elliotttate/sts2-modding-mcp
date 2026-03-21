using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Patches;

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
public static class HealPatch
{
    public static void Prefix(Creature creature, decimal amount, out int __state)
    {
        // Capture HP before heal so we can calculate actual healing done
        __state = creature.CurrentHp;
    }

    public static void Postfix(Creature creature, int __state)
    {
        if (!creature.IsPlayer || creature.Player == null) return;

        int actualHealing = creature.CurrentHp - __state;
        if (actualHealing > 0)
        {
            var stats = RunAwardsTracker.GetOrCreate(creature.Player.NetId);
            stats.TotalHealingDone += actualHealing;
        }
    }
}
