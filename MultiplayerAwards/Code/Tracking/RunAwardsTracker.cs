using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MultiplayerAwards.Awards;
using MultiplayerAwards.Networking;
using MultiplayerAwards.UI;

namespace MultiplayerAwards.Tracking;

public static class RunAwardsTracker
{
    private static readonly Dictionary<ulong, PlayerRunStats> _stats = new();
    private static readonly HashSet<ulong> _receivedFrom = new();
    private static bool _awardsShown;
    private static bool _syncStarted;
    private static int _expectedPlayers;

    public static IReadOnlyDictionary<ulong, PlayerRunStats> AllStats => _stats;
    public static bool AwardsShown => _awardsShown;

    public static PlayerRunStats GetOrCreate(ulong netId)
    {
        if (!_stats.TryGetValue(netId, out var stats))
        {
            stats = new PlayerRunStats { NetId = netId };
            _stats[netId] = stats;
        }
        return stats;
    }

    public static void Reset()
    {
        _stats.Clear();
        _receivedFrom.Clear();
        _awardsShown = false;
        _syncStarted = false;
        _expectedPlayers = 0;
        Log.Info("[MultiplayerAwards] Tracker reset for new run.");
    }

    public static void BroadcastMyStats()
    {
        if (_syncStarted) return;
        _syncStarted = true;

        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return;

            var runState = runManager.DebugOnlyGetState();
            if (runState == null) return;

            _expectedPlayers = runState.Players.Count;

            // Find the local player and snapshot final stats
            var localPlayer = LocalContext.GetMe(runState);
            if (localPlayer == null) return;

            var myStats = GetOrCreate(localPlayer.NetId);
            myStats.CharacterName = localPlayer.Character?.Id.Entry ?? "Unknown";
            myStats.TotalGoldAtEnd = localPlayer.Gold;

            // Capture display name if not already set
            if (string.IsNullOrEmpty(myStats.PlayerDisplayName))
            {
                try
                {
                    var platform = runManager.NetService?.Platform ?? MegaCrit.Sts2.Core.Platform.PlatformType.Steam;
                    myStats.PlayerDisplayName = MegaCrit.Sts2.Core.Platform.PlatformUtil.GetPlayerName(platform, localPlayer.NetId);
                }
                catch
                {
                    myStats.PlayerDisplayName = localPlayer.Creature?.Name ?? myStats.CharacterName;
                }
            }

            // Mark ourselves as received
            _receivedFrom.Add(localPlayer.NetId);

            // Broadcast to other players
            var msg = PlayerStatsMessage.FromStats(myStats);
            var netService = runManager.NetService;
            if (netService != null)
            {
                netService.SendMessage(msg);
                Log.Info("[MultiplayerAwards] Broadcast local stats to other players.");
            }

            // Start timeout - compute awards after 3 seconds even if not all received
            StartTimeoutTimer();

            // Check if we already have everyone (e.g., only 1 player)
            TryComputeAwards();
        }
        catch (Exception ex)
        {
            Log.Error($"[MultiplayerAwards] BroadcastMyStats error: {ex}");
        }
    }

    public static void OnStatsReceived(PlayerStatsMessage msg)
    {
        try
        {
            var stats = GetOrCreate(msg.SenderNetId);
            msg.ApplyTo(stats);
            _receivedFrom.Add(msg.SenderNetId);

            Log.Info($"[MultiplayerAwards] Received stats from {msg.CharacterName} ({msg.SenderNetId}). " +
                     $"Have {_receivedFrom.Count}/{_expectedPlayers}.");

            TryComputeAwards();
        }
        catch (Exception ex)
        {
            Log.Error($"[MultiplayerAwards] OnStatsReceived error: {ex}");
        }
    }

    private static void TryComputeAwards()
    {
        if (_awardsShown) return;
        if (_receivedFrom.Count < _expectedPlayers) return;

        ShowAwards();
    }

    private static void ShowAwards()
    {
        if (_awardsShown) return;
        _awardsShown = true;

        try
        {
            var awards = AwardEngine.ComputeAwards(_stats);
            Log.Info($"[MultiplayerAwards] Computed {awards.Count} awards for {_stats.Count} players.");

            // Show the awards screen on the main thread
            AwardsScreen.ShowAwards(awards, _stats);
        }
        catch (Exception ex)
        {
            Log.Error($"[MultiplayerAwards] ShowAwards error: {ex}");
        }
    }

    private static async void StartTimeoutTimer()
    {
        try
        {
            var tree = ((Godot.SceneTree)Godot.Engine.GetMainLoop());
            var timer = tree.CreateTimer(3.0);
            await tree.ToSignal(timer, "timeout");

            if (!_awardsShown && _receivedFrom.Count > 0)
            {
                Log.Info("[MultiplayerAwards] Timeout reached, showing awards with available stats.");
                ShowAwards();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[MultiplayerAwards] Timeout timer error: {ex}");
            // Fallback: show with what we have
            if (!_awardsShown && _receivedFrom.Count > 0)
                ShowAwards();
        }
    }
}
