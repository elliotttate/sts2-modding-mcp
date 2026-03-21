using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MultiplayerAwards.Awards;
using MultiplayerAwards.Tracking;
using MultiplayerAwards.UI;

namespace MultiplayerAwards.Test;

/// <summary>
/// Debug tool: press F9 in-game to show the awards screen with simulated multiplayer stats.
/// This bypasses multiplayer requirements so you can test the UI with a single player.
/// </summary>
public partial class TestAwardsTrigger : Node
{
    private static TestAwardsTrigger? _instance;

    public static void Register()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var root = tree.Root;
            if (root == null) return;

            if (_instance != null)
            {
                _instance.QueueFree();
                _instance = null;
            }

            _instance = new TestAwardsTrigger();
            _instance.Name = "TestAwardsTrigger";
            root.CallDeferred("add_child", _instance);
            Log.Info("[MultiplayerAwards] Test trigger registered. Press F9 to show test awards.");
        }
        catch (Exception ex)
        {
            Log.Error($"[MultiplayerAwards] Failed to register test trigger: {ex}");
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.F9)
            {
                Log.Info("[MultiplayerAwards] F9 pressed — showing test awards.");
                ShowTestAwards();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public static void ShowTestAwards()
    {
        try
        {
            ModEntry.WriteLog("ShowTestAwards() called");

            // Build fake stats for 4 simulated players
            var fakeStats = CreateFakeStats();
            ModEntry.WriteLog($"Created fake stats for {fakeStats.Count} players");

            // Compute awards
            var awards = AwardEngine.ComputeAwards(fakeStats);

            ModEntry.WriteLog($"Computed {awards.Count} awards:");
            foreach (var award in awards)
            {
                ModEntry.WriteLog($"  [{award.Award.Category}] {award.Award.Title} -> {award.WinnerName}: {award.Description}");
            }

            // Show the screen
            ModEntry.WriteLog("Calling AwardsScreen.ShowAwards...");
            AwardsScreen.ShowAwards(awards, fakeStats);
            ModEntry.WriteLog("AwardsScreen.ShowAwards completed");
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"ShowTestAwards ERROR: {ex}");
            Log.Error($"[MultiplayerAwards] Test awards error: {ex}");
        }
    }

    private static Dictionary<ulong, PlayerRunStats> CreateFakeStats()
    {
        var stats = new Dictionary<ulong, PlayerRunStats>();

        // Player 1: "Ironclad" - The heavy hitter / glass cannon
        var p1 = new PlayerRunStats
        {
            NetId = 1001,
            CharacterName = "Ironclad",
            PlayerDisplayName = "xXSlayerXx",
            TotalDamageDealt = 4521,
            TotalDamageTaken = 890,
            TotalDamageBlocked = 320,
            HighestSingleHit = 187,
            OverkillDamage = 312,
            TotalBlockGained = 480,
            BlockGivenToOthers = 0,
            TotalCardsPlayed = 245,
            AttackCardsPlayed = 180,
            SkillCardsPlayed = 50,
            PowerCardsPlayed = 15,
            CardsExhausted = 42,
            CardsDrawn = 390,
            MonstersKilled = 67,
            TotalEnergySpent = 310,
            PotionsUsed = 3,
            TotalGoldAtEnd = 45,
            TotalHealingDone = 85,
            TotalPowersApplied = 18,
            DebuffsAppliedToEnemies = 5,
            CombatsParticipated = 15,
            TurnsPlayed = 89,
            DeathCount = 2
        };
        stats[p1.NetId] = p1;

        // Player 2: "Silent" - The support / debuffer
        var p2 = new PlayerRunStats
        {
            NetId = 1002,
            CharacterName = "Silent",
            PlayerDisplayName = "SneakyPete",
            TotalDamageDealt = 1890,
            TotalDamageTaken = 210,
            TotalDamageBlocked = 890,
            HighestSingleHit = 45,
            OverkillDamage = 23,
            TotalBlockGained = 1450,
            BlockGivenToOthers = 620,
            TotalCardsPlayed = 310,
            AttackCardsPlayed = 90,
            SkillCardsPlayed = 190,
            PowerCardsPlayed = 30,
            CardsExhausted = 8,
            CardsDrawn = 520,
            MonstersKilled = 21,
            TotalEnergySpent = 380,
            PotionsUsed = 7,
            TotalGoldAtEnd = 312,
            TotalHealingDone = 240,
            TotalPowersApplied = 45,
            DebuffsAppliedToEnemies = 38,
            CombatsParticipated = 15,
            TurnsPlayed = 92,
            DeathCount = 0
        };
        stats[p2.NetId] = p2;

        // Player 3: "Defect" - The tank / block monster
        var p3 = new PlayerRunStats
        {
            NetId = 1003,
            CharacterName = "Defect",
            PlayerDisplayName = "TankMaster99",
            TotalDamageDealt = 2100,
            TotalDamageTaken = 1450,
            TotalDamageBlocked = 2100,
            HighestSingleHit = 92,
            OverkillDamage = 55,
            TotalBlockGained = 3200,
            BlockGivenToOthers = 1100,
            TotalCardsPlayed = 280,
            AttackCardsPlayed = 100,
            SkillCardsPlayed = 150,
            PowerCardsPlayed = 30,
            CardsExhausted = 5,
            CardsDrawn = 410,
            MonstersKilled = 34,
            TotalEnergySpent = 350,
            PotionsUsed = 2,
            TotalGoldAtEnd = 89,
            TotalHealingDone = 120,
            TotalPowersApplied = 35,
            DebuffsAppliedToEnemies = 12,
            CombatsParticipated = 15,
            TurnsPlayed = 95,
            DeathCount = 1
        };
        stats[p3.NetId] = p3;

        // Player 4: "Watcher" - The AFK / low contribution player
        var p4 = new PlayerRunStats
        {
            NetId = 1004,
            CharacterName = "Watcher",
            PlayerDisplayName = "ChillGamer",
            TotalDamageDealt = 620,
            TotalDamageTaken = 380,
            TotalDamageBlocked = 190,
            HighestSingleHit = 35,
            OverkillDamage = 8,
            TotalBlockGained = 290,
            BlockGivenToOthers = 40,
            TotalCardsPlayed = 98,
            AttackCardsPlayed = 45,
            SkillCardsPlayed = 40,
            PowerCardsPlayed = 13,
            CardsExhausted = 2,
            CardsDrawn = 180,
            MonstersKilled = 8,
            TotalEnergySpent = 120,
            PotionsUsed = 1,
            TotalGoldAtEnd = 520,
            TotalHealingDone = 15,
            TotalPowersApplied = 15,
            DebuffsAppliedToEnemies = 3,
            CombatsParticipated = 12,
            TurnsPlayed = 65,
            DeathCount = 3
        };
        stats[p4.NetId] = p4;

        return stats;
    }
}
