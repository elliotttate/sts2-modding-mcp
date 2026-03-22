using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;

namespace FoilCards.Patches;

/// <summary>
/// Patch game hooks as a reliable trigger for foil application during combat.
/// </summary>
[HarmonyPatch]
public static class HookPatches
{
    [HarmonyPatch(typeof(Hook), "BeforeCombatStart")]
    [HarmonyPostfix]
    public static void OnCombat()
    {
        Log.Warn("[FoilCards] Hook: BeforeCombatStart fired!");
        ModEntry.ApplyFoilToAllCards();
    }

    [HarmonyPatch(typeof(Hook), "BeforeHandDraw")]
    [HarmonyPostfix]
    public static void OnDraw()
    {
        ModEntry.ApplyFoilToAllCards();
    }

    [HarmonyPatch(typeof(Hook), "BeforePlayPhaseStart")]
    [HarmonyPostfix]
    public static void OnPlayPhase()
    {
        ModEntry.ApplyFoilToAllCards();
    }
}
