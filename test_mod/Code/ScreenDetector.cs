using System;
using System.Reflection;

namespace MCPTest;

/// <summary>
/// Detects the current game screen by checking ActiveScreenContext
/// and various game state flags.
/// </summary>
public static class ScreenDetector
{
    public sealed class CurrentScreenInfo
    {
        public string Screen { get; init; } = "UNKNOWN";
        public string Source { get; init; } = "unknown";
        public string? RoomType { get; init; }
        public string? ActiveScreenType { get; init; }
    }

    public static string GetCurrentScreen()
        => GetScreenInfo().Screen;

    public static CurrentScreenInfo GetScreenInfo()
    {
        try
        {
            // Check if in combat
            var cm = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
            if (cm != null && cm.IsInProgress)
            {
                return new CurrentScreenInfo
                {
                    Screen = cm.IsPlayPhase ? "COMBAT_PLAYER_TURN" : "COMBAT_ENEMY_TURN",
                    Source = "combat_manager",
                };
            }

            var runManager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
            var state = runManager.IsInProgress ? runManager.DebugOnlyGetState() : null;
            var roomTypeName = state?.CurrentRoom?.GetType().Name;

            var activeScreenInfo = DetectActiveScreen();
            if (activeScreenInfo != null)
            {
                return new CurrentScreenInfo
                {
                    Screen = activeScreenInfo.Screen,
                    Source = activeScreenInfo.Source,
                    RoomType = roomTypeName,
                    ActiveScreenType = activeScreenInfo.ActiveScreenType,
                };
            }

            if (!runManager.IsInProgress)
            {
                return new CurrentScreenInfo
                {
                    Screen = "MAIN_MENU",
                    Source = "fallback",
                };
            }

            if (state == null)
            {
                return new CurrentScreenInfo
                {
                    Screen = "LOADING",
                    Source = "run_state",
                };
            }

            if (state.CurrentRoom == null)
            {
                return new CurrentScreenInfo
                {
                    Screen = "MAP",
                    Source = "run_state",
                };
            }

            return new CurrentScreenInfo
            {
                Screen = roomTypeName switch
                {
                    "CombatRoom" => "COMBAT_LOADING",
                    "MapRoom" => "MAP",
                    "EventRoom" => "EVENT",
                    "MerchantRoom" => "SHOP",
                    "RestSiteRoom" => "REST_SITE",
                    "TreasureRoom" => "TREASURE",
                    _ => $"ROOM_{roomTypeName}",
                },
                Source = "room_type",
                RoomType = roomTypeName,
            };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"ScreenDetector error: {ex.Message}");
            return new CurrentScreenInfo
            {
                Screen = "UNKNOWN",
                Source = "error",
            };
        }
    }

    internal static bool TryGetActiveScreenObject(out object? screen, out string? screenTypeName)
    {
        screen = null;
        screenTypeName = null;

        try
        {
            var ascType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext, sts2");
            if (ascType == null)
                return false;

            var instanceProp = ascType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var instance = instanceProp?.GetValue(null);
            if (instance == null)
                return false;

            var getScreenMethod = ascType.GetMethod("GetCurrentScreen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? ascType.GetMethod("GetScreen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            screen = getScreenMethod?.Invoke(instance, null)
                ?? ascType.GetProperty("CurrentScreen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
            if (screen != null)
            {
                screenTypeName = screen.GetType().Name;
                return true;
            }
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"ScreenDetector: Active screen reflection failed: {ex.Message}");
        }

        return false;
    }

    internal static string MapScreenTypeName(string? screenTypeName)
    {
        if (string.IsNullOrWhiteSpace(screenTypeName))
            return "UNKNOWN";

        return screenTypeName switch
        {
            var s when s.Contains("CardSelection", StringComparison.OrdinalIgnoreCase)
                || s.Contains("SelectScreen", StringComparison.OrdinalIgnoreCase)
                || s.Contains("ChooseCard", StringComparison.OrdinalIgnoreCase)
                || s.Contains("ChooseACard", StringComparison.OrdinalIgnoreCase)
                || s.Contains("ChooseABundle", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Draft", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Grid", StringComparison.OrdinalIgnoreCase)
                || s.Contains("CardReward", StringComparison.OrdinalIgnoreCase)
                => "CARD_SELECTION",
            var s when s.Contains("Reward", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Loot", StringComparison.OrdinalIgnoreCase)
                || s.Contains("BossRelic", StringComparison.OrdinalIgnoreCase)
                => "REWARD",
            var s when s.Contains("Treasure", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                => "TREASURE",
            var s when s.Contains("Merchant", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Shop", StringComparison.OrdinalIgnoreCase)
                => "SHOP",
            var s when s.Contains("Rest", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Campfire", StringComparison.OrdinalIgnoreCase)
                => "REST_SITE",
            var s when s.Contains("Event", StringComparison.OrdinalIgnoreCase)
                => "EVENT",
            var s when s.Contains("Map", StringComparison.OrdinalIgnoreCase)
                => "MAP",
            var s when s.Contains("CharacterSelect", StringComparison.OrdinalIgnoreCase)
                || s.Contains("CharSelect", StringComparison.OrdinalIgnoreCase)
                => "CHARACTER_SELECT",
            var s when s.Contains("GameOver", StringComparison.OrdinalIgnoreCase)
                => "GAME_OVER",
            var s when s.Contains("Settings", StringComparison.OrdinalIgnoreCase)
                => "SETTINGS",
            var s when s.Contains("Timeline", StringComparison.OrdinalIgnoreCase)
                => "TIMELINE",
            var s when s.Contains("MainMenu", StringComparison.OrdinalIgnoreCase)
                => "MAIN_MENU",
            _ => $"MENU_{screenTypeName}",
        };
    }

    private static CurrentScreenInfo? DetectActiveScreen()
    {
        if (!TryGetActiveScreenObject(out _, out var screenTypeName))
            return null;

        var screen = MapScreenTypeName(screenTypeName);
        if (screen == "UNKNOWN")
            return null;

        return new CurrentScreenInfo
        {
            Screen = screen,
            Source = "active_screen_context",
            ActiveScreenType = screenTypeName,
        };
    }
}
