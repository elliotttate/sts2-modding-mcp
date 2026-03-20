using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;

namespace MCPTest;

public static class BridgeHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static object? _devConsole;
    private static MethodInfo? _processCommandMethod;

    public static string HandleRequest(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;
            var method = root.GetProperty("method").GetString() ?? "";
            var id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;

            string? cmdParam = null;
            if (root.TryGetProperty("params", out var paramsProp))
            {
                if (paramsProp.TryGetProperty("command", out var cmdProp))
                    cmdParam = cmdProp.GetString();
            }

            // State reads run on main thread for safety
            object? result = method switch
            {
                "ping" => GetPing(),
                "get_screen" => MainThreadDispatcher.Invoke(() => GetScreen()),
                "get_run_state" => MainThreadDispatcher.Invoke(() => GetRunState()),
                "get_combat_state" => MainThreadDispatcher.Invoke(() => GetCombatState()),
                "get_player_state" => MainThreadDispatcher.Invoke(() => GetPlayerState()),
                "get_map_state" => MainThreadDispatcher.Invoke(() => GetMapState()),
                "get_available_actions" => MainThreadDispatcher.Invoke(() => GetAvailableActions()),
                "play_card" => MainThreadDispatcher.Invoke(() => PlayCard(root)),
                "end_turn" => MainThreadDispatcher.Invoke(() => EndTurn()),
                "console" => ExecuteConsoleCommand(cmdParam ?? ""),
                "start_run" => StartRun(root),
                _ => new { error = $"Unknown method: {method}" },
            };

            return JsonSerializer.Serialize(new { result, id }, JsonOpts);
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"HandleRequest error: {ex.Message}");
            return JsonSerializer.Serialize(new { error = ex.Message, id = 0 }, JsonOpts);
        }
    }

    // ─── Ping ────────────────────────────────────────────────────────────────

    private static object GetPing()
    {
        try
        {
            return MainThreadDispatcher.Invoke<object>(() => new
            {
                status = "ok",
                mod = "MCPTest",
                version = "2.0.0",
                screen = ScreenDetector.GetCurrentScreen(),
                run_in_progress = RunManager.Instance.IsInProgress,
                in_combat = CombatManager.Instance?.IsInProgress ?? false,
                is_player_turn = CombatManager.Instance?.IsPlayPhase ?? false,
            });
        }
        catch
        {
            return new { status = "ok", mod = "MCPTest", version = "2.0.0" };
        }
    }

    // ─── Screen ──────────────────────────────────────────────────────────────

    private static object GetScreen()
    {
        return new { screen = ScreenDetector.GetCurrentScreen() };
    }

    // ─── Run State ───────────────────────────────────────────────────────────

    private static object GetRunState()
    {
        try
        {
            if (!RunManager.Instance.IsInProgress)
                return new { in_progress = false, screen = ScreenDetector.GetCurrentScreen() };

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
                return new { in_progress = false };

            var players = new List<object>();
            foreach (var p in state.Players)
            {
                players.Add(new
                {
                    net_id = p.NetId,
                    character = p.Character?.GetType().Name ?? "unknown",
                    hp = p.Creature?.CurrentHp ?? 0,
                    max_hp = p.Creature?.MaxHp ?? 0,
                    gold = p.Gold,
                    deck_size = p.Deck?.Cards.Count ?? 0,
                    relic_count = p.Relics?.Count ?? 0,
                    max_energy = p.PlayerCombatState?.MaxEnergy ?? 3,
                });
            }

            return new
            {
                in_progress = true,
                screen = ScreenDetector.GetCurrentScreen(),
                act = state.CurrentActIndex + 1,
                floor = state.TotalFloor,
                act_floor = state.ActFloor,
                ascension = state.AscensionLevel,
                seed = state.Rng?.StringSeed ?? "unknown",
                current_room = state.CurrentRoom?.GetType().Name ?? "none",
                player_count = state.Players.Count,
                players,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Combat State (with intent decomposition) ────────────────────────────

    private static object GetCombatState()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsInProgress)
                return new { in_combat = false, screen = ScreenDetector.GetCurrentScreen() };

            var combatState = cm.DebugOnlyGetState();
            if (combatState == null)
                return new { in_combat = false };

            // Enemies with intent decomposition
            var enemies = new List<object>();
            int enemyIdx = 0;
            foreach (var creature in combatState.Enemies)
            {
                var powers = creature.Powers
                    .Select(p => new { name = p.GetType().Name, amount = p.Amount, type = p.Type.ToString() })
                    .ToList();

                // Intent decomposition
                object? intent = null;
                try
                {
                    var move = creature.Monster?.NextMove;
                    if (move != null)
                    {
                        var intents = move.Intents;
                        var intentList = new List<object>();
                        if (intents != null)
                        {
                            var allTargets = combatState.Players.Select(p => p.Creature).Cast<Creature>();
                            foreach (var i in intents)
                            {
                                var intentObj = new Dictionary<string, object?>
                                {
                                    ["type"] = i.IntentType.ToString(),
                                };

                                if (i is AttackIntent atk)
                                {
                                    try
                                    {
                                        intentObj["damage"] = atk.GetSingleDamage(allTargets, creature);
                                        intentObj["hits"] = atk.Repeats + 1;
                                        intentObj["total_damage"] = atk.GetTotalDamage(allTargets, creature);
                                    }
                                    catch { }
                                }
                                intentList.Add(intentObj);
                            }
                        }
                        intent = new { move_id = move.Id, intents = intentList };
                    }
                }
                catch (Exception ex) { ModEntry.WriteLog($"Intent read error: {ex.Message}"); }

                enemies.Add(new
                {
                    index = enemyIdx++,
                    name = creature.Monster?.GetType().Name ?? "unknown",
                    hp = creature.CurrentHp,
                    max_hp = creature.MaxHp,
                    block = creature.Block,
                    is_alive = creature.IsAlive,
                    intent,
                    powers,
                });
            }

            // Players with full combat details
            var playerStates = new List<object>();
            foreach (var creature in combatState.Allies)
            {
                var player = creature.Player;
                if (player == null) continue;

                var pcs = player.PlayerCombatState;
                var hand = new List<object>();
                int cardIdx = 0;
                if (pcs?.Hand?.Cards != null)
                {
                    foreach (var c in pcs.Hand.Cards)
                    {
                        bool canPlay = false;
                        string unplayableReason = "";
                        try
                        {
                            canPlay = c.CanPlay(out var reason, out _);
                            if (!canPlay) unplayableReason = reason.ToString();
                        }
                        catch { }

                        // Determine valid targets
                        List<int>? validTargets = null;
                        if (canPlay && (c.TargetType == TargetType.AnyEnemy || c.TargetType == TargetType.AnyAlly))
                        {
                            validTargets = new List<int>();
                            var targets = c.TargetType == TargetType.AnyEnemy ? combatState.Enemies : combatState.Allies;
                            int tIdx = 0;
                            foreach (var t in targets)
                            {
                                if (t.IsAlive && c.IsValidTarget(t))
                                    validTargets.Add(tIdx);
                                tIdx++;
                            }
                        }

                        hand.Add(new
                        {
                            index = cardIdx++,
                            name = c.GetType().Name,
                            type = c.Type.ToString(),
                            energy_cost = (int)c.EnergyCost.Canonical,
                            can_play = canPlay,
                            unplayable_reason = canPlay ? null : unplayableReason,
                            target_type = c.TargetType.ToString(),
                            valid_targets = validTargets,
                            upgraded = c.CurrentUpgradeLevel > 0,
                        });
                    }
                }

                var powers = creature.Powers
                    .Select(p => new { name = p.GetType().Name, amount = p.Amount, type = p.Type.ToString() })
                    .ToList();

                playerStates.Add(new
                {
                    character = player.Character?.GetType().Name,
                    hp = creature.CurrentHp,
                    max_hp = creature.MaxHp,
                    block = creature.Block,
                    energy = pcs?.Energy ?? 0,
                    max_energy = pcs?.MaxEnergy ?? 0,
                    hand_size = hand.Count,
                    hand,
                    draw_pile = pcs?.DrawPile?.Cards.Count ?? 0,
                    discard_pile = pcs?.DiscardPile?.Cards.Count ?? 0,
                    exhaust_pile = pcs?.ExhaustPile?.Cards.Count ?? 0,
                    powers,
                });
            }

            return new
            {
                in_combat = true,
                screen = "COMBAT_PLAYER_TURN",
                round = combatState.RoundNumber,
                is_player_turn = cm.IsPlayPhase,
                enemies,
                players = playerStates,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Player State ────────────────────────────────────────────────────────

    private static object GetPlayerState()
    {
        try
        {
            if (!RunManager.Instance.IsInProgress)
                return new { error = "No run in progress" };

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null) return new { error = "No run state" };

            var players = new List<object>();
            foreach (var p in state.Players)
            {
                var deck = new List<object>();
                if (p.Deck?.Cards != null)
                {
                    foreach (var c in p.Deck.Cards)
                        deck.Add(new { name = c.GetType().Name, type = c.Type.ToString(), rarity = c.Rarity.ToString(), energy_cost = (int)c.EnergyCost.Canonical, upgraded = c.CurrentUpgradeLevel > 0 });
                }

                var relics = new List<object>();
                if (p.Relics != null)
                {
                    foreach (var r in p.Relics)
                        relics.Add(new { name = r.GetType().Name, rarity = r.Rarity.ToString() });
                }

                var potions = new List<object>();
                for (int i = 0; i < p.MaxPotionCount; i++)
                {
                    try
                    {
                        var pot = p.Potions.ElementAtOrDefault(i);
                        potions.Add(pot != null
                            ? (object)new { slot = i, name = pot.GetType().Name, rarity = pot.Rarity.ToString() }
                            : new { slot = i, name = "empty" });
                    }
                    catch { }
                }

                players.Add(new
                {
                    net_id = p.NetId,
                    character = p.Character?.GetType().Name,
                    hp = p.Creature?.CurrentHp ?? 0,
                    max_hp = p.Creature?.MaxHp ?? 0,
                    gold = p.Gold,
                    deck_count = deck.Count,
                    deck, relics, potions,
                });
            }

            return new { players };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Map State ───────────────────────────────────────────────────────────

    private static object GetMapState()
    {
        try
        {
            if (!RunManager.Instance.IsInProgress)
                return new { error = "No run in progress" };

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.Map == null) return new { error = "No map available" };

            var map = state.Map;
            var visited = new HashSet<string>(state.VisitedMapCoords.Select(c => $"{c.row},{c.col}"));

            var nodes = new List<object>();
            foreach (var point in map.GetAllMapPoints())
            {
                var coord = $"{point.coord.row},{point.coord.col}";
                var children = point.Children?.Select(c => $"{c.coord.row},{c.coord.col}").ToList() ?? new List<string>();

                bool isAvailable = false;
                if (state.VisitedMapCoords.Count == 0)
                {
                    // Start of act - starting node is available
                    isAvailable = point.coord.row == map.StartingMapPoint.coord.row
                                && point.coord.col == map.StartingMapPoint.coord.col;
                }
                else
                {
                    // Children of last visited node
                    var lastVisited = state.VisitedMapCoords.Last();
                    var lastPoint = map.GetPoint(lastVisited);
                    isAvailable = lastPoint?.Children?.Contains(point) ?? false;
                }

                nodes.Add(new
                {
                    row = point.coord.row,
                    col = point.coord.col,
                    type = point.PointType.ToString(),
                    visited = visited.Contains(coord),
                    available = isAvailable,
                    children,
                });
            }

            return new
            {
                act = state.CurrentActIndex + 1,
                floor = state.TotalFloor,
                node_count = nodes.Count,
                nodes,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Available Actions ───────────────────────────────────────────────────

    private static object GetAvailableActions()
    {
        try
        {
            var screen = ScreenDetector.GetCurrentScreen();
            var actions = new List<object>();

            if (screen.StartsWith("COMBAT"))
            {
                var cm = CombatManager.Instance;
                if (cm?.IsInProgress == true && cm.IsPlayPhase)
                {
                    var combatState = cm.DebugOnlyGetState();
                    if (combatState != null)
                    {
                        // Playable cards
                        foreach (var creature in combatState.Allies)
                        {
                            var player = creature.Player;
                            if (player?.PlayerCombatState?.Hand?.Cards == null) continue;

                            int cardIdx = 0;
                            foreach (var card in player.PlayerCombatState.Hand.Cards)
                            {
                                if (card.CanPlay())
                                {
                                    if (card.TargetType == TargetType.AnyEnemy)
                                    {
                                        int enemyIdx = 0;
                                        foreach (var enemy in combatState.Enemies)
                                        {
                                            if (enemy.IsAlive && card.IsValidTarget(enemy))
                                            {
                                                actions.Add(new
                                                {
                                                    action = "play_card",
                                                    card_index = cardIdx,
                                                    target_index = enemyIdx,
                                                    card_name = card.GetType().Name,
                                                    target_name = enemy.Monster?.GetType().Name,
                                                });
                                            }
                                            enemyIdx++;
                                        }
                                    }
                                    else
                                    {
                                        actions.Add(new
                                        {
                                            action = "play_card",
                                            card_index = cardIdx,
                                            target_index = (int?)null,
                                            card_name = card.GetType().Name,
                                            target_name = (string?)null,
                                        });
                                    }
                                }
                                cardIdx++;
                            }
                        }

                        // End turn is always available during player turn
                        actions.Add(new { action = "end_turn" });
                    }
                }
            }
            else if (screen == "MAP")
            {
                var state = RunManager.Instance.DebugOnlyGetState();
                if (state?.Map != null)
                {
                    // Available map nodes
                    if (state.VisitedMapCoords.Count == 0)
                    {
                        var start = state.Map.StartingMapPoint;
                        foreach (var child in start.Children)
                        {
                            actions.Add(new
                            {
                                action = "travel",
                                node = $"{child.coord.row},{child.coord.col}",
                                type = child.PointType.ToString(),
                            });
                        }
                    }
                    else
                    {
                        var lastVisited = state.VisitedMapCoords.Last();
                        var lastPoint = state.Map.GetPoint(lastVisited);
                        if (lastPoint?.Children != null)
                        {
                            foreach (var child in lastPoint.Children)
                            {
                                actions.Add(new
                                {
                                    action = "travel",
                                    node = $"{child.coord.row},{child.coord.col}",
                                    type = child.PointType.ToString(),
                                });
                            }
                        }
                    }
                }
            }

            actions.Add(new { action = "console", description = "Execute any console command" });

            return new { screen, action_count = actions.Count, actions };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Console Command ─────────────────────────────────────────────────────

    // ─── Play Card ─────────────────────────────────────────────────────────

    private static object PlayCard(JsonElement root)
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsInProgress || !cm.IsPlayPhase)
                return new { error = "Not in combat or not player turn" };

            int cardIndex = 0;
            int targetIndex = -1;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("card_index", out var ci)) cardIndex = ci.GetInt32();
                if (p.TryGetProperty("target_index", out var ti)) targetIndex = ti.GetInt32();
            }

            var combatState = cm.DebugOnlyGetState();
            if (combatState == null) return new { error = "No combat state" };

            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            if (player?.PlayerCombatState?.Hand?.Cards == null)
                return new { error = "No hand available" };

            var handCards = player.PlayerCombatState.Hand.Cards.ToList();
            if (cardIndex < 0 || cardIndex >= handCards.Count)
                return new { error = $"Card index {cardIndex} out of range (hand size: {handCards.Count})" };

            var card = handCards[cardIndex];
            var cardName = card.GetType().Name;

            if (!card.CanPlay(out var reason, out _))
                return new { error = $"Card {cardName} cannot be played: {reason}" };

            // Resolve target
            Creature? target = null;
            if (card.TargetType == TargetType.AnyEnemy && targetIndex >= 0)
            {
                var enemies = combatState.Enemies.ToList();
                if (targetIndex < enemies.Count)
                    target = enemies[targetIndex];
                else
                    return new { error = $"Target index {targetIndex} out of range (enemies: {enemies.Count})" };
            }
            else if (card.TargetType == TargetType.AnyAlly && targetIndex >= 0)
            {
                var allies = combatState.Allies.ToList();
                if (targetIndex < allies.Count)
                    target = allies[targetIndex];
            }

            // Play the card
            bool played = card.TryManualPlay(target);
            ModEntry.WriteLog($"[PlayCard] {cardName} target={target?.Monster?.GetType().Name ?? target?.Player?.Character?.GetType().Name ?? "none"} => {played}");

            return new { success = played, card = cardName, card_index = cardIndex, target_index = targetIndex };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"PlayCard error: {ex.Message}");
            return new { error = ex.Message };
        }
    }

    // ─── End Turn ────────────────────────────────────────────────────────────

    private static object EndTurn()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsInProgress || !cm.IsPlayPhase)
                return new { error = "Not in combat or not player turn" };

            var state = RunManager.Instance.DebugOnlyGetState();
            var player = LocalContext.GetMe(state);
            if (player == null) return new { error = "No player" };

            var combatState = cm.DebugOnlyGetState();
            if (combatState == null) return new { error = "No combat state" };

            var roundNumber = combatState.RoundNumber;
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
                new EndPlayerTurnAction(player, roundNumber));

            ModEntry.WriteLog($"[EndTurn] Round {roundNumber}");
            return new { success = true, round = roundNumber };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"EndTurn error: {ex.Message}");
            return new { error = ex.Message };
        }
    }

    // ─── Console Command ─────────────────────────────────────────────────────

    private static object ExecuteConsoleCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new { error = "No command provided" };

        try
        {
            EnsureConsoleAccess();
            if (_devConsole == null || _processCommandMethod == null)
                return new { error = "DevConsole not available" };

            // Dispatch to main thread and wait for result
            MainThreadDispatcher.Post(() =>
            {
                try
                {
                    _processCommandMethod!.Invoke(_devConsole, new object[] { command });
                    ModEntry.WriteLog($"Console (main thread): {command}");
                }
                catch (Exception ex2)
                {
                    ModEntry.WriteLog($"Console error: {ex2.Message}");
                }
            });

            return new { success = true, command };
        }
        catch (Exception ex) { return new { error = ex.Message, command }; }
    }

    // ─── Start Run ───────────────────────────────────────────────────────────

    private static object StartRun(JsonElement root)
    {
        try
        {
            if (RunManager.Instance.IsInProgress)
                return new { error = "A run is already in progress" };

            string characterName = "Ironclad";
            int ascension = 0;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("character", out var cProp))
                    characterName = cProp.GetString() ?? "Ironclad";
                if (p.TryGetProperty("ascension", out var aProp))
                    ascension = aProp.GetInt32();
            }

            // Find character
            CharacterModel? charModel = null;
            foreach (var ch in ModelDb.AllCharacters)
            {
                if (ch.GetType().Name.Equals(characterName, StringComparison.OrdinalIgnoreCase))
                    { charModel = ch; break; }
            }
            if (charModel == null)
            {
                var available = string.Join(", ", ModelDb.AllCharacters.Select(c => c.GetType().Name));
                return new { error = $"Character '{characterName}' not found. Available: {available}" };
            }

            var acts = ModelDb.Acts.ToList();
            var seed = DateTime.Now.Ticks.ToString();
            var emptyModifiers = new List<ModifierModel>();

            // Dispatch to main thread
            var nGameType = Type.GetType("MegaCrit.Sts2.Core.Nodes.NGame, sts2");
            var instanceProp = nGameType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var startMethod = nGameType?.GetMethod("StartNewSingleplayerRun", BindingFlags.Public | BindingFlags.Instance);

            if (nGameType == null || instanceProp == null || startMethod == null)
                return new { error = "NGame API not found" };

            MainThreadDispatcher.Post(() =>
            {
                try
                {
                    var nGame = instanceProp.GetValue(null);
                    if (nGame == null) { ModEntry.WriteLog("NGame.Instance is null"); return; }

                    var task = startMethod.Invoke(nGame, new object?[] {
                        charModel, true,
                        (IReadOnlyList<ActModel>)acts,
                        (IReadOnlyList<ModifierModel>)emptyModifiers,
                        seed, ascension, null
                    });
                    if (task is System.Threading.Tasks.Task t)
                        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(t);

                    ModEntry.WriteLog($"StartRun dispatched: {characterName} asc={ascension}");
                }
                catch (Exception ex2) { ModEntry.WriteLog($"StartRun main thread: {ex2.Message}"); }
            });

            return new { success = true, character = characterName, ascension, seed };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Console Access ──────────────────────────────────────────────────────

    private static void EnsureConsoleAccess()
    {
        if (_devConsole != null && _processCommandMethod != null) return;

        try
        {
            var nDevConsoleType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole, sts2");
            if (nDevConsoleType == null) return;

            var instanceField = nDevConsoleType.GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            var nDevConsole = instanceField?.GetValue(null);
            if (nDevConsole == null) return;

            var consoleField = nDevConsoleType.GetField("_devConsole",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _devConsole = consoleField?.GetValue(nDevConsole);

            if (_devConsole != null)
            {
                _processCommandMethod = _devConsole.GetType().GetMethod("ProcessCommand",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(string) }, null);
            }
        }
        catch (Exception ex) { ModEntry.WriteLog($"Console access error: {ex.Message}"); }
    }
}
