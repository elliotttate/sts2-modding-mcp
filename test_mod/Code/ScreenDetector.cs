using System;
using System.Reflection;

namespace MCPTest;

/// <summary>
/// Detects the current game screen by checking ActiveScreenContext
/// and various game state flags.
/// </summary>
public static class ScreenDetector
{
    public static string GetCurrentScreen()
    {
        try
        {
            // Check if in combat
            var cm = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
            if (cm != null && cm.IsInProgress)
            {
                return cm.IsPlayPhase ? "COMBAT_PLAYER_TURN" : "COMBAT_ENEMY_TURN";
            }

            // Check if run in progress
            if (!MegaCrit.Sts2.Core.Runs.RunManager.Instance.IsInProgress)
            {
                // Try to detect main menu vs character select
                return DetectMenuScreen();
            }

            // Run is in progress, check room type
            var state = MegaCrit.Sts2.Core.Runs.RunManager.Instance.DebugOnlyGetState();
            if (state == null) return "LOADING";

            var room = state.CurrentRoom;
            if (room == null) return "MAP";

            var roomTypeName = room.GetType().Name;
            return roomTypeName switch
            {
                "CombatRoom" => "COMBAT_LOADING",
                "MapRoom" => "MAP",
                "EventRoom" => "EVENT",
                "MerchantRoom" => "SHOP",
                "RestSiteRoom" => "REST_SITE",
                "TreasureRoom" => "TREASURE",
                _ => $"ROOM_{roomTypeName}",
            };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"ScreenDetector error: {ex.Message}");
            return "UNKNOWN";
        }
    }

    private static string DetectMenuScreen()
    {
        try
        {
            // Try ActiveScreenContext
            var ascType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext, sts2");
            if (ascType != null)
            {
                var instanceProp = ascType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var instance = instanceProp?.GetValue(null);
                if (instance != null)
                {
                    var getScreenMethod = ascType.GetMethod("GetCurrentScreen");
                    var screen = getScreenMethod?.Invoke(instance, null);
                    if (screen != null)
                    {
                        var screenTypeName = screen.GetType().Name;
                        return screenTypeName switch
                        {
                            var s when s.Contains("MainMenu") => "MAIN_MENU",
                            var s when s.Contains("CharacterSelect") || s.Contains("CharSelect") => "CHARACTER_SELECT",
                            var s when s.Contains("GameOver") => "GAME_OVER",
                            var s when s.Contains("Settings") => "SETTINGS",
                            var s when s.Contains("Timeline") => "TIMELINE",
                            var s when s.Contains("Map") => "MAP",
                            var s when s.Contains("Reward") => "REWARD",
                            var s when s.Contains("CardSelection") => "CARD_SELECTION",
                            _ => $"MENU_{screenTypeName}",
                        };
                    }
                }
            }
        }
        catch { }

        return "MAIN_MENU";
    }
}
