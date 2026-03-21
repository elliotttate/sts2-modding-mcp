using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Patches;

/// <summary>
/// Resets the awards tracker when a new run begins.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
public static class RunStartPatch
{
    public static void Prefix()
    {
        RunAwardsTracker.Reset();
    }
}
