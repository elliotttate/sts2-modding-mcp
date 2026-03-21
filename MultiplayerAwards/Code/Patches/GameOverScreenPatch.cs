using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Patches;

[HarmonyPatch(typeof(NGameOverScreen), nameof(NGameOverScreen.AfterOverlayOpened))]
public static class GameOverScreenPatch
{
    public static void Postfix(NGameOverScreen __instance)
    {
        if (RunAwardsTracker.AwardsShown) return;

        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return;

            var runState = runManager.DebugOnlyGetState();
            if (runState == null) return;

            // Only show in multiplayer (2+ players)
            if (runState.Players.Count <= 1) return;

            Log.Info($"[MultiplayerAwards] Game over detected with {runState.Players.Count} players. Starting awards sync.");

            // Snapshot end-of-run data for all players we can see locally
            var platform = runManager.NetService?.Platform ?? PlatformType.Steam;
            foreach (var player in runState.Players)
            {
                var stats = RunAwardsTracker.GetOrCreate(player.NetId);
                stats.CharacterName = player.Character?.Id.Entry ?? "Unknown";
                stats.TotalGoldAtEnd = player.Gold;

                // Get the player's display name (Steam name, etc.)
                try
                {
                    stats.PlayerDisplayName = PlatformUtil.GetPlayerName(platform, player.NetId);
                }
                catch
                {
                    stats.PlayerDisplayName = player.Creature?.Name ?? stats.CharacterName;
                }
            }

            // Broadcast our stats and trigger the sync flow
            RunAwardsTracker.BroadcastMyStats();
        }
        catch (System.Exception ex)
        {
            Log.Error($"[MultiplayerAwards] GameOverScreenPatch error: {ex}");
        }
    }
}
