using System;
using System.IO;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MultiplayerAwards.Networking;
using MultiplayerAwards.Test;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MultiplayerAwards", "awards.log");

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            WriteLog("=== MultiplayerAwards v1.0 Initializing ===");

            // Apply all Harmony patches
            _harmony = new Harmony("com.elliotttate.multiplayerawards");
            _harmony.PatchAll();
            WriteLog("Harmony patches applied.");

            // Register for run events — RunManager.Instance may be null at this point
            try
            {
                var runManager = RunManager.Instance;
                if (runManager != null)
                {
                    runManager.RunStarted += OnRunStarted;
                    WriteLog("Subscribed to RunManager.RunStarted.");
                }
                else
                {
                    WriteLog("RunManager.Instance is null — will subscribe later via deferred call.");
                    // Defer subscription to next frame when RunManager should be ready
                    DeferRunManagerSubscription();
                }
            }
            catch (Exception ex)
            {
                WriteLog($"RunManager subscription deferred: {ex.Message}");
                DeferRunManagerSubscription();
            }

            // Register test trigger (F9 to show test awards) — defer to ensure scene tree is ready
            DeferTestTrigger();

            Log.Info("[MultiplayerAwards] v1.0 loaded! Press F9 to test awards screen.");
            WriteLog("=== MultiplayerAwards v1.0 Loaded ===");
        }
        catch (Exception ex)
        {
            Log.Error($"[MultiplayerAwards] Init failed: {ex}");
            WriteLog($"FATAL ERROR: {ex}");
        }
    }

    private static async void DeferRunManagerSubscription()
    {
        try
        {
            // Wait a frame for RunManager to initialize
            var tree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
            if (tree != null)
            {
                await tree.ToSignal(tree, "process_frame");
            }

            var runManager = RunManager.Instance;
            if (runManager != null)
            {
                runManager.RunStarted += OnRunStarted;
                WriteLog("Deferred: Subscribed to RunManager.RunStarted.");
            }
            else
            {
                WriteLog("WARNING: RunManager.Instance still null after deferral.");
            }
        }
        catch (Exception ex)
        {
            WriteLog($"DeferRunManagerSubscription error: {ex.Message}");
        }
    }

    private static async void DeferTestTrigger()
    {
        try
        {
            // Wait a frame for scene tree to be fully ready
            var tree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
            if (tree != null)
            {
                await tree.ToSignal(tree, "process_frame");
            }

            TestAwardsTrigger.Register();
            WriteLog("Test trigger registered (F9).");
        }
        catch (Exception ex)
        {
            WriteLog($"DeferTestTrigger error: {ex.Message}");
        }
    }

    private static void OnRunStarted(RunState runState)
    {
        try
        {
            // Register net message handler for receiving stats from other players
            var netService = RunManager.Instance?.NetService;
            if (netService != null)
            {
                netService.RegisterMessageHandler<PlayerStatsMessage>(OnPlayerStatsReceived);
                WriteLog($"Registered PlayerStatsMessage handler. Players: {runState.Players.Count}");
            }

            // Initialize tracking for all players
            foreach (var player in runState.Players)
            {
                var stats = RunAwardsTracker.GetOrCreate(player.NetId);
                stats.CharacterName = player.Character?.Id.Entry ?? "Unknown";
            }

            WriteLog($"Run started with {runState.Players.Count} players.");
        }
        catch (Exception ex)
        {
            WriteLog($"OnRunStarted error: {ex.Message}");
        }
    }

    private static void OnPlayerStatsReceived(PlayerStatsMessage message, ulong senderId)
    {
        WriteLog($"Received stats from {message.CharacterName} (NetId: {message.SenderNetId})");
        RunAwardsTracker.OnStatsReceived(message);
    }

    public static void WriteLog(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }
}
