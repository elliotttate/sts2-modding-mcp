using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using Godot;
using GodotEngine = Godot.Engine;

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
    private static readonly string[] ProceedMethodNames = ["Proceed", "Continue", "Confirm", "Done", "Close", "Leave", "Accept"];
    private static readonly string[] ConfirmMethodNames = ["Confirm", "Submit", "Accept", "Done", "CompleteSelection"];
    private static readonly string[] SkipMethodNames = ["Skip", "Cancel", "Decline", "BowlSkip"];
    private static Dictionary<string, object?>? _previousState;
    private static readonly Dictionary<string, Dictionary<string, object?>> _snapshots = new();
    private static Dictionary<string, object?>? _lastRunFixtureParams;

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
                "get_diagnostics" => MainThreadDispatcher.Invoke(() => GetDiagnostics(root)),
                "get_log" => GetBridgeLog(root),
                "play_card" => MainThreadDispatcher.Invoke(() => PlayCard(root)),
                "end_turn" => MainThreadDispatcher.Invoke(() => EndTurn()),
                "console" => ExecuteConsoleCommand(cmdParam ?? ""),
                "start_run" => StartRun(root),
                "execute_action" => MainThreadDispatcher.Invoke(() => ExecuteAction(root)),
                "use_potion" => MainThreadDispatcher.Invoke(() => UsePotion(root)),
                "make_event_choice" => MainThreadDispatcher.Invoke(() => MakeEventChoice(root)),
                "navigate_map" => MainThreadDispatcher.Invoke(() => NavigateMap(root)),
                "rest_site_choice" => MainThreadDispatcher.Invoke(() => RestSiteChoice(root)),
                "shop_action" => MainThreadDispatcher.Invoke(() => ShopAction(root)),
                "get_card_piles" => MainThreadDispatcher.Invoke(() => GetCardPiles()),
                "manipulate_state" => MainThreadDispatcher.Invoke(() => ManipulateState(root)),
                "hot_swap_patches" => MainThreadDispatcher.Invoke(() => HotSwapPatches(root)),
                "get_exceptions" => GetExceptions(root),
                "get_state_diff" => MainThreadDispatcher.Invoke(() => GetStateDiff()),
                "capture_screenshot" => MainThreadDispatcher.Invoke(() => CaptureScreenshot(root)),
                "get_events" => GetEvents(root),
                "save_snapshot" => MainThreadDispatcher.Invoke(() => SaveSnapshot(root)),
                "restore_snapshot" => MainThreadDispatcher.Invoke(() => RestoreSnapshot(root)),
                "set_game_speed" => MainThreadDispatcher.Invoke(() => SetGameSpeed(root)),
                "restart_run" => RestartRun(),
                "debug_pause" => DebugPause(),
                "debug_resume" => DebugResume(),
                "debug_step" => DebugStep(root),
                "debug_set_breakpoint" => DebugSetBreakpoint(root),
                "debug_remove_breakpoint" => DebugRemoveBreakpoint(root),
                "debug_list_breakpoints" => DebugListBreakpoints(),
                "debug_clear_breakpoints" => DebugClearBreakpoints(),
                "debug_get_context" => DebugGetContext(),
                "get_game_log" => GetGameLog(root),
                "set_log_level" => SetLogLevel(root),
                "get_log_levels" => GetLogLevels(),
                "clear_exceptions" => ClearExceptions(),
                "clear_events" => ClearEvents(),
                "autoslay_start" => AutoSlayStart(root),
                "autoslay_stop" => MainThreadDispatcher.Invoke(() => AutoSlayStop()),
                "autoslay_status" => MainThreadDispatcher.Invoke(() => AutoSlayGetStatus()),
                "autoslay_configure" => AutoSlayConfigure(root),
                "navigate_menu" => MainThreadDispatcher.Invoke(() => NavigateMenu(root)),
                "find_cards" => MainThreadDispatcher.Invoke(() => FindCards(root)),
                "card_tilt_test" => MainThreadDispatcher.Invoke(() => CardTiltTest(root)),
                "start_auto_rotate" => MainThreadDispatcher.Invoke(() => StartAutoRotate()),
                "stop_auto_rotate" => MainThreadDispatcher.Invoke(() => StopAutoRotate()),
                "start_card_tilt_loop" => StartCardTiltLoop(),
                "stop_card_tilt_loop" => StopCardTiltLoop(),
                "start_foil_tilt" => MainThreadDispatcher.Invoke(() => StartFoilTilt()),
                "stop_foil_tilt" => MainThreadDispatcher.Invoke(() => StopFoilTilt()),
                "click_node" => MainThreadDispatcher.Invoke(() => ClickNode(root)),
                _ => new { error = $"Unknown method: {method}" },
            };

            return JsonSerializer.Serialize(new { result, id }, JsonOpts);
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"HandleRequest error: {ex.Message}\n{ex.StackTrace}");
            try
            {
                return JsonSerializer.Serialize(new { error = ex.Message, id = 0 }, JsonOpts);
            }
            catch
            {
                // Last resort if serialization itself fails
                return "{\"error\":\"Internal serialization failure\",\"id\":0}";
            }
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
        catch (Exception ex)
        {
            ModEntry.WriteLog($"Ping game state read error: {ex.Message}");
            return new { status = "ok", mod = "MCPTest", version = "2.0.0" };
        }
    }

    // ─── Screen ──────────────────────────────────────────────────────────────

    private static object GetScreen()
    {
        var info = ScreenDetector.GetScreenInfo();
        return new
        {
            screen = info.Screen,
            screen_source = info.Source,
            room_type = info.RoomType,
            screen_context_type = info.ActiveScreenType,
        };
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
                                    catch (Exception ex) { ModEntry.WriteLog($"Intent damage calc error: {ex.Message}"); }
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
                        catch (Exception ex) { ModEntry.WriteLog($"CanPlay check error: {ex.Message}"); }

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
                    catch (Exception ex) { ModEntry.WriteLog($"Potion read error slot {i}: {ex.Message}"); }
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
            var screenInfo = ScreenDetector.GetScreenInfo();
            var screen = screenInfo.Screen;
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
            else if (screen == "EVENT")
            {
                actions.AddRange(GetEventActionDescriptors());
            }
            else if (screen == "REWARD")
            {
                actions.AddRange(GetRewardActionDescriptors());
            }
            else if (screen == "SHOP")
            {
                actions.AddRange(GetShopActionDescriptors());
            }
            else if (screen == "REST_SITE")
            {
                actions.AddRange(GetRestActionDescriptors());
            }
            else if (screen == "TREASURE")
            {
                actions.AddRange(GetTreasureActionDescriptors());
            }
            else if (screen == "CARD_SELECTION")
            {
                actions.AddRange(GetCardSelectionActionDescriptors());
            }

            actions.Add(new { action = "console", description = "Execute any console command" });

            return new
            {
                screen,
                screen_source = screenInfo.Source,
                room_type = screenInfo.RoomType,
                screen_context_type = screenInfo.ActiveScreenType,
                action_count = actions.Count,
                actions,
            };
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
            EventTracker.Record("play_card", cardName, new Dictionary<string, object?> { ["index"] = cardIndex, ["target"] = targetIndex, ["played"] = played });

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
            EventTracker.Record("end_turn", $"Round {roundNumber}");
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
            string seed = DateTime.Now.Ticks.ToString();
            var fixtureCommands = new List<string>();
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("character", out var cProp))
                    characterName = cProp.GetString() ?? "Ironclad";
                if (p.TryGetProperty("ascension", out var aProp))
                    ascension = aProp.GetInt32();
                if (p.TryGetProperty("seed", out var sProp))
                    seed = sProp.ValueKind == JsonValueKind.String ? (sProp.GetString() ?? seed) : sProp.ToString();

                fixtureCommands = BuildFixtureCommands(p);
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
            var emptyModifiers = new List<ModifierModel>();

            // Dispatch to main thread
            var nGameType = Type.GetType("MegaCrit.Sts2.Core.Nodes.NGame, sts2");
            var instanceProp = nGameType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var startMethod = nGameType?.GetMethod("StartNewSingleplayerRun", BindingFlags.Public | BindingFlags.Instance);

            if (nGameType == null || instanceProp == null || startMethod == null)
                return new { error = "NGame API not found" };

            // Dispatch to main thread synchronously so run initiation completes before we respond
            string? dispatchError = null;
            MainThreadDispatcher.Invoke(() =>
            {
                try
                {
                    var nGame = instanceProp.GetValue(null);
                    if (nGame == null) { dispatchError = "NGame.Instance is null"; return; }

                    var task = startMethod.Invoke(nGame, new object?[] {
                        charModel, true,
                        (IReadOnlyList<ActModel>)acts,
                        (IReadOnlyList<ModifierModel>)emptyModifiers,
                        seed, ascension, null
                    });
                    if (task is System.Threading.Tasks.Task t)
                    {
                        t.ContinueWith(finishedTask =>
                        {
                            if (finishedTask.IsFaulted)
                            {
                                ModEntry.WriteLog($"StartRun task failed: {finishedTask.Exception?.GetBaseException().Message}");
                                return;
                            }

                            EventTracker.Record("run_started", $"{characterName} asc={ascension} seed={seed}");

                            if (fixtureCommands.Count > 0)
                                MainThreadDispatcher.Post(() => ApplyConsoleCommands(fixtureCommands, "start_run_fixture"));
                        });
                        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(t);
                    }
                    else if (fixtureCommands.Count > 0)
                    {
                        MainThreadDispatcher.Post(() => ApplyConsoleCommands(fixtureCommands, "start_run_fixture"));
                    }

                    ModEntry.WriteLog($"StartRun dispatched: {characterName} asc={ascension} seed={seed} fixtures={fixtureCommands.Count}");
                }
                catch (Exception ex2)
                {
                    dispatchError = ex2.Message;
                    ModEntry.WriteLog($"StartRun main thread: {ex2.Message}");
                }
            });

            if (dispatchError != null)
                return new { error = dispatchError };

            // Store params for restart_run
            _lastRunFixtureParams = new Dictionary<string, object?>
            {
                ["character"] = characterName,
                ["ascension"] = ascension,
                ["seed"] = seed,
            };

            return new
            {
                success = true,
                character = characterName,
                ascension,
                seed,
                fixture_command_count = fixtureCommands.Count,
                fixture_commands = fixtureCommands,
            };
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

    // ─── Use Potion ─────────────────────────────────────────────────────────

    private static object UsePotion(JsonElement root)
    {
        try
        {
            int potionIndex = 0;
            int targetIndex = -1;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("potion_index", out var pi)) potionIndex = pi.GetInt32();
                if (p.TryGetProperty("target_index", out var ti)) targetIndex = ti.GetInt32();
            }

            if (!RunManager.Instance.IsInProgress)
                return new { error = "No run in progress" };

            var state = RunManager.Instance.DebugOnlyGetState();
            var player = LocalContext.GetMe(state);
            if (player == null) return new { error = "No player" };

            var potions = player.Potions.ToList();
            if (potionIndex < 0 || potionIndex >= potions.Count)
                return new { error = $"Potion index {potionIndex} out of range (have {potions.Count})" };

            var potion = potions[potionIndex];
            if (potion == null)
                return new { error = $"Potion slot {potionIndex} is empty" };

            var potionName = potion.GetType().Name;

            // Resolve target for targeted potions
            Creature? target = null;
            if (targetIndex >= 0 && CombatManager.Instance?.IsInProgress == true)
            {
                var combatState = CombatManager.Instance.DebugOnlyGetState();
                if (combatState != null)
                {
                    if (potion.TargetType == TargetType.AnyEnemy)
                    {
                        var enemies = combatState.Enemies.ToList();
                        if (targetIndex < enemies.Count) target = enemies[targetIndex];
                    }
                    else if (potion.TargetType == TargetType.AnyAlly)
                    {
                        var allies = combatState.Allies.ToList();
                        if (targetIndex < allies.Count) target = allies[targetIndex];
                    }
                }
            }

            // Use the potion via console command as direct API is complex
            EnsureConsoleAccess();
            if (_processCommandMethod != null && _devConsole != null)
            {
                _processCommandMethod.Invoke(_devConsole, new object[] { $"potion use {potionIndex}" });
            }

            ModEntry.WriteLog($"[UsePotion] {potionName} index={potionIndex} target={targetIndex}");
            return new { success = true, potion = potionName, potion_index = potionIndex, target_index = targetIndex };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object DiscardPotion(JsonElement p)
    {
        try
        {
            var potionIndex = p.TryGetProperty("potion_index", out var pi) ? pi.GetInt32() : 0;

            if (!RunManager.Instance.IsInProgress)
                return new { error = "No run in progress" };

            var state = RunManager.Instance.DebugOnlyGetState();
            var player = LocalContext.GetMe(state);
            if (player == null) return new { error = "No player" };

            var potions = player.Potions.ToList();
            if (potionIndex < 0 || potionIndex >= potions.Count)
                return new { error = $"Potion index {potionIndex} out of range (have {potions.Count})" };

            var potion = potions[potionIndex];
            if (potion == null)
                return new { error = $"Potion slot {potionIndex} is empty" };

            var potionName = potion.GetType().Name;

            EnsureConsoleAccess();
            if (_processCommandMethod != null && _devConsole != null)
                _processCommandMethod.Invoke(_devConsole, new object[] { $"potion discard {potionIndex}" });

            ModEntry.WriteLog($"[DiscardPotion] {potionName} index={potionIndex}");
            return new { success = true, potion = potionName, potion_index = potionIndex };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Event Choice ───────────────────────────────────────────────────────

    private static object MakeEventChoice(JsonElement root)
    {
        try
        {
            int choiceIndex = 0;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("choice_index", out var ci)) choiceIndex = ci.GetInt32();
            }
            return ExecuteEventChoice(choiceIndex);
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Navigate Map ───────────────────────────────────────────────────────

    private static object NavigateMap(JsonElement root)
    {
        try
        {
            int row = 0, col = 0;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("row", out var rProp)) row = rProp.GetInt32();
                if (p.TryGetProperty("col", out var cProp)) col = cProp.GetInt32();
            }
            return ExecuteMapTravel(row, col);
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Rest Site Choice ───────────────────────────────────────────────────

    private static object RestSiteChoice(JsonElement root)
    {
        try
        {
            string choice = "rest";
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("choice", out var cProp))
                    choice = cProp.GetString() ?? "rest";
            }
            return ExecuteRestAction(choice);
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Shop Action ────────────────────────────────────────────────────────

    private static object ShopAction(JsonElement root)
    {
        try
        {
            string action = "buy_card";
            int index = 0;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("action", out var aProp))
                    action = aProp.GetString() ?? "buy_card";
                if (p.TryGetProperty("index", out var iProp))
                    index = iProp.GetInt32();
            }
            return ExecuteShopAction(action, index);
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Card Piles ─────────────────────────────────────────────────────────

    private static object GetCardPiles()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsInProgress)
                return new { error = "Not in combat" };

            var combatState = cm.DebugOnlyGetState();
            if (combatState == null) return new { error = "No combat state" };

            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            if (player?.PlayerCombatState == null)
                return new { error = "No player combat state" };

            var pcs = player.PlayerCombatState;

            Func<IEnumerable<CardModel>, List<object>> mapCards = (cards) =>
                cards.Select((c, i) => (object)new
                {
                    index = i,
                    name = c.GetType().Name,
                    type = c.Type.ToString(),
                    energy_cost = (int)c.EnergyCost.Canonical,
                    upgraded = c.CurrentUpgradeLevel > 0,
                }).ToList();

            var hand = mapCards(pcs.Hand?.Cards ?? Enumerable.Empty<CardModel>());
            var draw = mapCards(pcs.DrawPile?.Cards ?? Enumerable.Empty<CardModel>());
            var discard = mapCards(pcs.DiscardPile?.Cards ?? Enumerable.Empty<CardModel>());
            var exhaust = mapCards(pcs.ExhaustPile?.Cards ?? Enumerable.Empty<CardModel>());

            return new
            {
                hand = new { count = hand.Count, cards = hand },
                draw_pile = new { count = draw.Count, cards = draw },
                discard_pile = new { count = discard.Count, cards = discard },
                exhaust_pile = new { count = exhaust.Count, cards = exhaust },
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Manipulate State ───────────────────────────────────────────────────

    private static object ManipulateState(JsonElement root)
    {
        try
        {
            if (!RunManager.Instance.IsInProgress)
                return new { error = "No run in progress" };

            var applied = new List<string>();

            if (root.TryGetProperty("params", out var p))
            {
                applied = BuildFixtureCommands(p);
                ApplyConsoleCommands(applied, "manipulate_state");
            }

            ModEntry.WriteLog($"[ManipulateState] Applied {applied.Count} changes");
            return new { success = true, applied_count = applied.Count, applied };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Overlay-Aware Dismiss Handlers ─────────────────────────────────────

    private static object DismissRewardScreen()
    {
        try
        {
            var overlay = NOverlayStack.Instance?.Peek();
            if (overlay is NRewardsScreen rewardsScreen)
            {
                // Try the proceed button first
                var proceedBtn = rewardsScreen.GetNodeOrNull<NButton>("%ProceedButton");
                if (proceedBtn == null) proceedBtn = rewardsScreen.GetNodeOrNull<NButton>("ProceedButton");
                if (proceedBtn == null)
                {
                    // Search all NButton descendants for one named Proceed/Skip
                    foreach (var child in GetAllDescendants(rewardsScreen))
                    {
                        if (child is NButton btn && btn.IsVisibleInTree())
                        {
                            var n = btn.Name.ToString().ToLower();
                            if (n.Contains("proceed") || n.Contains("skip") || n.Contains("continue"))
                            {
                                proceedBtn = btn;
                                break;
                            }
                        }
                    }
                }
                if (proceedBtn != null && proceedBtn.IsVisibleInTree())
                {
                    proceedBtn.ForceClick();
                    return new { success = true, action = "reward_dismiss", invoked = "overlay:ForceClick(ProceedButton)" };
                }
            }

            // Fallback: try console
            EnsureConsoleAccess();
            if (_processCommandMethod != null && _devConsole != null)
            {
                _processCommandMethod.Invoke(_devConsole, new object[] { "skip" });
                return new { success = true, action = "reward_dismiss", invoked = "console:skip" };
            }

            return new { success = false, error = "Could not find proceed/skip button on reward screen" };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object DismissCardSelectionScreen()
    {
        try
        {
            var overlay = NOverlayStack.Instance?.Peek();

            // NChooseACardSelectionScreen has a "SkipButton" child
            if (overlay is Godot.Node overlayNode)
            {
                var skipBtn = overlayNode.GetNodeOrNull<NClickableControl>("SkipButton");
                if (skipBtn == null) skipBtn = overlayNode.GetNodeOrNull<NClickableControl>("%SkipButton");
                if (skipBtn == null)
                {
                    foreach (var child in GetAllDescendants(overlayNode))
                    {
                        if (child is NClickableControl ctrl && ctrl.IsVisibleInTree())
                        {
                            var n = ctrl.Name.ToString().ToLower();
                            if (n.Contains("skip"))
                            {
                                skipBtn = ctrl;
                                break;
                            }
                        }
                    }
                }
                if (skipBtn != null && skipBtn.IsVisibleInTree())
                {
                    skipBtn.ForceClick();
                    return new { success = true, action = "card_skip", invoked = "overlay:ForceClick(SkipButton)" };
                }
            }

            // Fallback: console skip
            EnsureConsoleAccess();
            if (_processCommandMethod != null && _devConsole != null)
            {
                _processCommandMethod.Invoke(_devConsole, new object[] { "skip" });
                return new { success = true, action = "card_skip", invoked = "console:skip" };
            }

            return new { success = false, error = "Could not find skip button on card selection screen" };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static List<Godot.Node> GetAllDescendants(Godot.Node root)
    {
        var result = new List<Godot.Node>();
        var stack = new Stack<Godot.Node>();
        foreach (var child in root.GetChildren()) stack.Push(child);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            result.Add(node);
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
        return result;
    }

    // ─── Generic Actions / Diagnostics ─────────────────────────────────────

    private static object ExecuteAction(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("params", out var p))
                return new { error = "execute_action requires params" };

            var action = p.TryGetProperty("action", out var aProp)
                ? (aProp.GetString() ?? "").Trim().Replace(' ', '_').ToLowerInvariant()
                : "";
            if (string.IsNullOrWhiteSpace(action))
                return new { error = "execute_action requires a non-empty action" };

            return action switch
            {
                "travel" or "map_travel" or "navigate_map" => NavigateMap(root),
                "event_option" or "make_event_choice" => MakeEventChoice(root),
                "event_proceed" => ProceedCurrentScreen("event_proceed", "proceed", "continue"),
                "reward_select" or "take_reward" or "claim_reward" => ExecuteRewardSelection(p),
                "reward_proceed" or "reward_skip" => DismissRewardScreen(),
                "shop_buy" => ExecuteShopAction(MapShopBuyAction(p), p.TryGetProperty("index", out var si) ? si.GetInt32() : 0),
                "shop_proceed" => ProceedCurrentScreen("shop_proceed", "leave", "proceed"),
                "rest_option" => ExecuteRestAction(p.TryGetProperty("choice", out var rc) ? (rc.GetString() ?? "rest") : "rest"),
                "rest_proceed" => ProceedCurrentScreen("rest_proceed", "proceed", "continue"),
                "treasure_pick" or "treasure_select" => ExecuteTreasureSelection(p),
                "treasure_proceed" => ProceedCurrentScreen("treasure_proceed", "proceed", "continue", "open"),
                "card_select" => ExecuteCardSelection(p),
                "card_confirm" => ConfirmCurrentScreen("card_confirm", "confirm", "proceed"),
                "card_skip" => DismissCardSelectionScreen(),
                "discard_potion" => DiscardPotion(p),
                "proceed" => ProceedCurrentScreen("proceed", "proceed", "continue", "leave"),
                _ => new { error = $"Unsupported action '{action}'" },
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object GetDiagnostics(JsonElement root)
    {
        try
        {
            int logLines = 40;
            if (root.TryGetProperty("params", out var p) && p.TryGetProperty("log_lines", out var ll))
                logLines = Math.Clamp(ll.GetInt32(), 1, 200);

            var screenInfo = ScreenDetector.GetScreenInfo();
            var state = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
            var screenObject = GetActiveScreenObject();
            var eventObject = GetCurrentEventObject();

            return new
            {
                status = "ok",
                screen = screenInfo.Screen,
                screen_source = screenInfo.Source,
                room_type = screenInfo.RoomType,
                screen_context_type = screenInfo.ActiveScreenType,
                run_in_progress = RunManager.Instance.IsInProgress,
                in_combat = CombatManager.Instance?.IsInProgress ?? false,
                is_player_turn = CombatManager.Instance?.IsPlayPhase ?? false,
                floor = state?.TotalFloor,
                act = state != null ? state.CurrentActIndex + 1 : (int?)null,
                current_room = state?.CurrentRoom?.GetType().Name,
                active_screen = DescribeObjectShape(screenObject),
                current_event = DescribeObjectShape(eventObject),
                log_path = ModEntry.GetLogPath(),
                recent_log = ReadBridgeLogLines(logLines, null),
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object GetBridgeLog(JsonElement root)
    {
        try
        {
            int lines = 200;
            string? contains = null;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("lines", out var lProp))
                    lines = Math.Clamp(lProp.GetInt32(), 1, 1000);
                if (p.TryGetProperty("contains", out var cProp))
                    contains = cProp.GetString();
            }

            var logPath = ModEntry.GetLogPath();
            return new
            {
                log_path = logPath,
                lines = ReadBridgeLogLines(lines, contains),
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Concrete Action Executors ─────────────────────────────────────────

    private static object ExecuteEventChoice(int choiceIndex)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        if (screen != "EVENT")
            return new { error = $"Not in an event (current screen: {screen})" };

        var eventObj = GetCurrentEventObject();
        if (eventObj != null)
        {
            var options = GetItemsFromMethods(eventObj, "GetCurrentOptions", "GetOptions");
            if (options.Count == 0)
                options = GetItemsFromMembers(eventObj, "CurrentOptions", "Options", "Choices", "Entries");

            if (TryInvokeMethod(eventObj, ["ChooseOption", "SelectOption", "OnOptionSelected", "ProceedWithOption"], [choiceIndex], out var invokedMethod))
            {
                ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via {invokedMethod}");
                return new { success = true, choice_index = choiceIndex, invoked = invokedMethod };
            }

            if (choiceIndex >= 0 && choiceIndex < options.Count)
            {
                var option = options[choiceIndex];
                if (TryInvokeMethod(eventObj, ["ChooseOption", "SelectOption", "OnOptionSelected", "ProceedWithOption"], [option], out invokedMethod)
                    || TryInvokeMethod(option, ["Select", "Choose", "Click", "Invoke"], Array.Empty<object?>(), out invokedMethod))
                {
                    ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via {invokedMethod}");
                    return new { success = true, choice_index = choiceIndex, invoked = invokedMethod, label = GetReadableLabel(option) };
                }
            }
        }

        // Fallback: try to find option buttons on the NEventRoom Godot node itself
        // (handles Neow and other events with non-standard option models)
        var screenNodeResult = TrySelectScreenOptionButton(choiceIndex);
        if (screenNodeResult != null)
            return screenNodeResult;

        if (TryExecuteConsoleCommand($"event_choose {choiceIndex}"))
        {
            ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via console");
            return new { success = true, choice_index = choiceIndex, invoked = "console:event_choose" };
        }

        return new { error = $"Unable to choose event option {choiceIndex}" };
    }

    /// <summary>
    /// Fallback event option selection: walk the active screen's Godot node tree
    /// to find option buttons (handles Neow and other non-standard event screens).
    /// Looks for _connectedOptions, child Button/BaseButton nodes, or option containers.
    /// </summary>
    private static object? TrySelectScreenOptionButton(int choiceIndex)
    {
        try
        {
            if (!ScreenDetector.TryGetActiveScreenObject(out var screenObj, out _) || screenObj == null)
                return null;

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            // Strategy 1: Look for _connectedOptions or _options fields (list/array of option nodes)
            string[] optionFieldNames = ["_connectedOptions", "_options", "_optionButtons", "_choices", "_modifierOptions"];
            foreach (var fieldName in optionFieldNames)
            {
                var field = screenObj.GetType().GetField(fieldName, flags);
                if (field == null) continue;

                var fieldValue = field.GetValue(screenObj);
                if (fieldValue == null) continue;

                var items = new List<object>();
                if (fieldValue is IList list)
                    foreach (var item in list) { if (item != null) items.Add(item); }
                else if (fieldValue is IEnumerable enumerable)
                    foreach (var item in enumerable) { if (item != null) items.Add(item); }

                if (choiceIndex >= 0 && choiceIndex < items.Count)
                {
                    var button = items[choiceIndex];
                    // Try event option / Godot button interaction methods
                    // EventOption.Chosen() is the standard STS2 event choice method (returns Task)
                    if (TryInvokeMethod(button, ["Chosen", "Choose", "Select", "OnPressed", "Press", "_Pressed"], Array.Empty<object?>(), out var invokedMethod))
                    {
                        ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via screen node {fieldName}[{choiceIndex}].{invokedMethod}");
                        return new { success = true, choice_index = choiceIndex, invoked = $"screen_node:{fieldName}.{invokedMethod}", label = GetReadableLabel(button) };
                    }

                    // Try EmitSignal("pressed") for Godot BaseButton nodes
                    if (button is Godot.BaseButton godotButton)
                    {
                        godotButton.EmitSignal("pressed");
                        ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via Godot BaseButton.EmitSignal('pressed') on {fieldName}[{choiceIndex}]");
                        return new { success = true, choice_index = choiceIndex, invoked = $"godot_button:{fieldName}[{choiceIndex}]", label = GetReadableLabel(button) };
                    }

                    // Try invoking with the index as parameter
                    if (TryInvokeMethod(screenObj, ["SelectOption", "ChooseOption", "OnOptionSelected", "_OnOptionSelected"], [choiceIndex], out invokedMethod))
                    {
                        ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via screen.{invokedMethod}({choiceIndex})");
                        return new { success = true, choice_index = choiceIndex, invoked = $"screen_method:{invokedMethod}" };
                    }
                    if (TryInvokeMethod(screenObj, ["SelectOption", "ChooseOption", "OnOptionSelected", "_OnOptionSelected"], [button], out invokedMethod))
                    {
                        ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via screen.{invokedMethod}(option)");
                        return new { success = true, choice_index = choiceIndex, invoked = $"screen_method:{invokedMethod}" };
                    }

                    ModEntry.WriteLog($"[EventChoice] Found {items.Count} items in {fieldName} but couldn't invoke option {choiceIndex} (type: {button.GetType().Name})");
                }
                else if (items.Count > 0)
                {
                    ModEntry.WriteLog($"[EventChoice] {fieldName} has {items.Count} items but index {choiceIndex} is out of range");
                }
            }

            // Strategy 2: Try calling SelectOption/ChooseOption on the screen node directly with index
            if (TryInvokeMethod(screenObj, ["SelectOption", "ChooseOption", "OnOptionSelected", "_OnOptionSelected", "SelectModifier", "_OnModifierSelected"], [choiceIndex], out var directMethod))
            {
                ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via screen.{directMethod}({choiceIndex})");
                return new { success = true, choice_index = choiceIndex, invoked = $"screen_direct:{directMethod}" };
            }

            // Strategy 3: Walk child nodes looking for buttons
            if (screenObj is Godot.Node node)
            {
                var buttons = new List<Godot.BaseButton>();
                CollectButtons(node, buttons, depth: 4);

                if (choiceIndex >= 0 && choiceIndex < buttons.Count)
                {
                    buttons[choiceIndex].EmitSignal("pressed");
                    ModEntry.WriteLog($"[EventChoice] index={choiceIndex} via child button walk ({buttons.Count} buttons found), pressed {buttons[choiceIndex].Name}");
                    return new { success = true, choice_index = choiceIndex, invoked = $"child_button:{buttons[choiceIndex].Name}", button_count = buttons.Count };
                }

                if (buttons.Count > 0)
                    ModEntry.WriteLog($"[EventChoice] Found {buttons.Count} child buttons but index {choiceIndex} out of range");
            }
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"[EventChoice] Screen node fallback error: {ex.Message}");
        }

        return null;
    }

    private static void CollectButtons(Godot.Node parent, List<Godot.BaseButton> buttons, int depth)
    {
        if (depth <= 0) return;
        foreach (var child in parent.GetChildren())
        {
            if (child is Godot.BaseButton btn && btn.Visible)
                buttons.Add(btn);
            CollectButtons(child, buttons, depth - 1);
        }
    }

    private static object ExecuteMapTravel(int row, int col)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        if (screen != "MAP")
            return new { error = $"Not on map (current screen: {screen})" };

        if (!RunManager.Instance.IsInProgress)
            return new { error = "No run in progress" };

        var state = RunManager.Instance.DebugOnlyGetState();
        if (state?.Map == null)
            return new { error = "No map available" };

        // Try direct NMapScreen.OnMapPointSelectedLocally for reliable travel
        var mapScreen = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance;
        if (mapScreen != null && mapScreen.IsOpen)
        {
            // Find travelable NMapPoint nodes matching the target coordinates
            var mapPoints = GetAllDescendants(mapScreen)
                .OfType<MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint>()
                .Where(mp => mp.Point?.coord.row == row && mp.Point?.coord.col == col)
                .ToList();

            if (mapPoints.Count > 0)
            {
                var target = mapPoints[0];
                mapScreen.OnMapPointSelectedLocally(target);
                ModEntry.WriteLog($"[NavigateMap] Direct travel to ({row},{col}) type={target.Point?.PointType}");
                return new { success = true, row, col, type = target.Point?.PointType.ToString() ?? "Unknown", method = "direct" };
            }
        }

        // Fallback: console travel command
        if (TryExecuteConsoleCommand($"travel {row},{col}"))
        {
            var targetPoint = state.Map.GetAllMapPoints()
                .FirstOrDefault(mp => mp.coord.row == row && mp.coord.col == col);
            ModEntry.WriteLog($"[NavigateMap] Console travel to ({row},{col}) type={targetPoint?.PointType}");
            return new { success = true, row, col, type = targetPoint?.PointType.ToString() ?? "Unknown", method = "console" };
        }

        return new { error = "Could not travel to map node" };
    }

    private static object ExecuteRestAction(string choice)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        if (screen != "REST_SITE")
            return new { error = $"Not at rest site (current screen: {screen})" };

        var normalized = choice.Trim().ToLowerInvariant();
        if (normalized is "proceed" or "continue")
            return ProceedCurrentScreen("rest_proceed", "proceed", "continue");

        var screenObj = GetActiveScreenObject();
        if (screenObj != null)
        {
            var pascalChoice = ToPascalCase(normalized);
            if (TryInvokeMethod(screenObj,
                    [pascalChoice, $"Choose{pascalChoice}", $"Select{pascalChoice}", $"On{pascalChoice}", "SelectOption"],
                    [normalized],
                    out var invokedMethod)
                || TryInvokeMethod(screenObj,
                    [pascalChoice, $"Choose{pascalChoice}", $"Select{pascalChoice}", $"On{pascalChoice}"],
                    Array.Empty<object?>(),
                    out invokedMethod))
            {
                ModEntry.WriteLog($"[RestSite] choice={choice} via {invokedMethod}");
                return new { success = true, choice = normalized, invoked = invokedMethod };
            }
        }

        var cmd = normalized switch
        {
            "rest" or "heal" => "heal 30",
            "smith" or "upgrade" => "upgrade",
            "recall" => "recall",
            "dig" => "dig",
            "lift" => "lift",
            _ => normalized,
        };

        if (!TryExecuteConsoleCommand(cmd))
            return new { error = "DevConsole not available for rest action", choice = normalized };

        ModEntry.WriteLog($"[RestSite] choice={choice} via console");
        return new { success = true, choice = normalized, invoked = $"console:{cmd}" };
    }

    private static object ExecuteShopAction(string action, int index)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        if (screen != "SHOP")
            return new { error = $"Not in shop (current screen: {screen})" };

        var normalized = action.Trim().ToLowerInvariant();
        if (normalized is "proceed" or "leave" or "shop_proceed")
            return ProceedCurrentScreen("shop_proceed", "leave", "proceed");

        string cmd = normalized switch
        {
            "shop_buy" or "buy_card" => $"shop buy_card {index}",
            "buy_relic" => $"shop buy_relic {index}",
            "buy_potion" => $"shop buy_potion {index}",
            "remove_card" or "purge" => "shop remove",
            _ => normalized,
        };

        if (!TryExecuteConsoleCommand(cmd))
            return new { error = "DevConsole not available for shop action", action = normalized, index };

        ModEntry.WriteLog($"[ShopAction] {normalized} index={index}");
        return new { success = true, action = normalized, index, invoked = $"console:{cmd}" };
    }

    private static object ExecuteRewardSelection(JsonElement p)
    {
        var index = p.TryGetProperty("index", out var idx)
            ? idx.GetInt32()
            : p.TryGetProperty("reward_index", out var rIdx) ? rIdx.GetInt32() : 0;

        return ExecuteIndexedScreenInteraction(
            expectedScreen: "REWARD",
            actionName: "reward_select",
            index: index,
            collectionMembers: ["Rewards", "RewardItems", "AvailableRewards", "Entries", "Choices", "Options"],
            collectionMethods: ["GetRewards", "GetCurrentRewards", "GetChoices"],
            screenMethods: ["SelectReward", "ChooseReward", "TakeReward", "ClaimReward", "Select", "Choose", "OnRewardClicked"],
            itemMethods: ["Select", "Choose", "Take", "Claim", "Click", "Invoke"]);
    }

    private static object ExecuteTreasureSelection(JsonElement p)
    {
        var index = p.TryGetProperty("index", out var idx)
            ? idx.GetInt32()
            : p.TryGetProperty("treasure_index", out var tIdx) ? tIdx.GetInt32() : 0;

        return ExecuteIndexedScreenInteraction(
            expectedScreen: "TREASURE",
            actionName: "treasure_pick",
            index: index,
            collectionMembers: ["Rewards", "Treasure", "Contents", "Choices", "Options", "Relics"],
            collectionMethods: ["GetRewards", "GetContents", "GetChoices"],
            screenMethods: ["SelectTreasure", "ChooseTreasure", "TakeTreasure", "OpenTreasure", "Select", "Choose", "OnTreasureClicked"],
            itemMethods: ["Select", "Choose", "Take", "Open", "Click", "Invoke"]);
    }

    private static object ExecuteCardSelection(JsonElement p)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        if (screen != "CARD_SELECTION")
            return new { error = $"Not in card selection (current screen: {screen})" };

        var confirmAfterSelection = p.TryGetProperty("confirm", out var confirmProp) && confirmProp.GetBoolean();
        var indices = new List<int>();

        if (p.TryGetProperty("indices", out var indicesProp) && indicesProp.ValueKind == JsonValueKind.Array)
            indices.AddRange(indicesProp.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt32()));
        if (p.TryGetProperty("card_indices", out var cardIndicesProp) && cardIndicesProp.ValueKind == JsonValueKind.Array)
            indices.AddRange(cardIndicesProp.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt32()));
        if (indices.Count == 0 && p.TryGetProperty("index", out var indexProp))
            indices.Add(indexProp.GetInt32());
        if (indices.Count == 0 && p.TryGetProperty("card_index", out var cardIndexProp))
            indices.Add(cardIndexProp.GetInt32());

        if (indices.Count == 0 && !confirmAfterSelection)
            return new { error = "card_select requires index/indices or confirm=true" };

        var screenObj = GetActiveScreenObject();
        if (screenObj == null)
            return new { error = "No active card selection screen object" };

        var cards = GetItemsFromMembers(screenObj, "Cards", "CardChoices", "Choices", "SelectableCards", "Options");
        if (cards.Count == 0)
            cards = GetItemsFromMethods(screenObj, "GetCards", "GetChoices");

        var results = new List<object>();
        foreach (var index in indices.Distinct())
        {
            string? invokedMethod = null;
            string label = index >= 0 && index < cards.Count ? GetReadableLabel(cards[index]) : $"card_{index}";

            var selected =
                TryInvokeMethod(screenObj, ["SelectCard", "ChooseCard", "ToggleCardSelection", "Select", "Choose", "OnCardClicked"], [index], out invokedMethod)
                || (index >= 0 && index < cards.Count
                    && (TryInvokeMethod(screenObj, ["SelectCard", "ChooseCard", "ToggleCardSelection", "Select", "Choose", "OnCardClicked"], [cards[index]], out invokedMethod)
                        || TryInvokeMethod(cards[index], ["Select", "Choose", "ToggleSelection", "Click", "Invoke"], Array.Empty<object?>(), out invokedMethod)));

            results.Add(new
            {
                index,
                label,
                success = selected,
                invoked = invokedMethod,
            });
        }

        object? confirmResult = null;
        if (confirmAfterSelection)
            confirmResult = ConfirmCurrentScreen("card_confirm", "confirm", "proceed");

        ModEntry.WriteLog($"[CardSelection] selected={indices.Count} confirm={confirmAfterSelection}");
        return new
        {
            success = results.All(r => (bool)(r.GetType().GetProperty("success")?.GetValue(r) ?? false))
                && (!confirmAfterSelection || !HasError(confirmResult)),
            selected_count = results.Count,
            results,
            confirm = confirmResult,
        };
    }

    private static object ProceedCurrentScreen(string requestedAction, params string[] consoleFallbacks)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        var screenObj = GetActiveScreenObject();

        if (screenObj != null && TryInvokeMethod(screenObj, ProceedMethodNames, Array.Empty<object?>(), out var invokedMethod))
        {
            ModEntry.WriteLog($"[{requestedAction}] via {invokedMethod}");
            return new { success = true, action = requestedAction, screen, invoked = invokedMethod };
        }

        foreach (var cmd in consoleFallbacks.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (TryExecuteConsoleCommand(cmd))
            {
                ModEntry.WriteLog($"[{requestedAction}] via console:{cmd}");
                return new { success = true, action = requestedAction, screen, invoked = $"console:{cmd}" };
            }
        }

        return new { error = $"Unable to execute {requestedAction}", screen, screen_object = screenObj?.GetType().Name };
    }

    private static object ConfirmCurrentScreen(string requestedAction, params string[] consoleFallbacks)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        var screenObj = GetActiveScreenObject();

        if (screenObj != null && TryInvokeMethod(screenObj, ConfirmMethodNames, Array.Empty<object?>(), out var invokedMethod))
        {
            ModEntry.WriteLog($"[{requestedAction}] via {invokedMethod}");
            return new { success = true, action = requestedAction, screen, invoked = invokedMethod };
        }

        return ProceedCurrentScreen(requestedAction, consoleFallbacks);
    }

    private static object SkipCurrentScreen(string requestedAction, params string[] consoleFallbacks)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        var screenObj = GetActiveScreenObject();

        if (screenObj != null && TryInvokeMethod(screenObj, SkipMethodNames, Array.Empty<object?>(), out var invokedMethod))
        {
            ModEntry.WriteLog($"[{requestedAction}] via {invokedMethod}");
            return new { success = true, action = requestedAction, screen, invoked = invokedMethod };
        }

        foreach (var cmd in consoleFallbacks.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (TryExecuteConsoleCommand(cmd))
            {
                ModEntry.WriteLog($"[{requestedAction}] via console:{cmd}");
                return new { success = true, action = requestedAction, screen, invoked = $"console:{cmd}" };
            }
        }

        return new { error = $"Unable to execute {requestedAction}", screen, screen_object = screenObj?.GetType().Name };
    }

    // ─── Action Discovery ───────────────────────────────────────────────────

    private static List<object> GetEventActionDescriptors()
    {
        var actions = new List<object>();
        var eventObj = GetCurrentEventObject();
        var options = GetItemsFromMethods(eventObj, "GetCurrentOptions", "GetOptions");
        if (options.Count == 0)
            options = GetItemsFromMembers(eventObj, "CurrentOptions", "Options", "Choices", "Entries");

        for (int i = 0; i < options.Count; i++)
        {
            actions.Add(new
            {
                action = "event_option",
                choice_index = i,
                label = GetReadableLabel(options[i]),
                option_type = options[i].GetType().Name,
            });
        }

        // Fallback: if no options found from event model, look at the screen node's UI buttons
        if (actions.Count == 0)
        {
            try
            {
                if (ScreenDetector.TryGetActiveScreenObject(out var screenObj, out _) && screenObj != null)
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                    string[] fieldNames = ["_connectedOptions", "_options", "_optionButtons", "_choices", "_modifierOptions"];
                    foreach (var fieldName in fieldNames)
                    {
                        var field = screenObj.GetType().GetField(fieldName, flags);
                        if (field == null) continue;
                        var fieldValue = field.GetValue(screenObj);
                        if (fieldValue == null) continue;

                        var items = new List<object>();
                        if (fieldValue is IEnumerable enumerable)
                            foreach (var item in enumerable) { if (item != null) items.Add(item); }

                        for (int i = 0; i < items.Count; i++)
                        {
                            actions.Add(new
                            {
                                action = "event_option",
                                choice_index = i,
                                label = GetReadableLabel(items[i]),
                                option_type = items[i].GetType().Name,
                                source = fieldName,
                            });
                        }
                        if (items.Count > 0) break; // Use the first field that has options
                    }

                    // If still nothing, try child buttons
                    if (actions.Count == 0 && screenObj is Godot.Node node)
                    {
                        var buttons = new List<Godot.BaseButton>();
                        CollectButtons(node, buttons, depth: 4);
                        for (int i = 0; i < buttons.Count; i++)
                        {
                            actions.Add(new
                            {
                                action = "event_option",
                                choice_index = i,
                                label = buttons[i].Name.ToString(),
                                option_type = buttons[i].GetType().Name,
                                source = "child_buttons",
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"GetEventActionDescriptors screen fallback error: {ex.Message}");
            }
        }

        actions.Add(new { action = "event_proceed" });
        return actions;
    }

    private static List<object> GetRewardActionDescriptors()
    {
        var actions = new List<object>();
        var screenObj = GetActiveScreenObject();
        var rewards = GetItemsFromMembers(screenObj, "Rewards", "RewardItems", "AvailableRewards", "Entries", "Choices", "Options");
        if (rewards.Count == 0)
            rewards = GetItemsFromMethods(screenObj, "GetRewards", "GetCurrentRewards", "GetChoices");

        for (int i = 0; i < rewards.Count; i++)
        {
            actions.Add(new
            {
                action = "reward_select",
                reward_index = i,
                label = GetReadableLabel(rewards[i]),
                reward_type = rewards[i].GetType().Name,
            });
        }

        actions.Add(new { action = "reward_proceed" });
        return actions;
    }

    private static List<object> GetShopActionDescriptors()
    {
        var actions = new List<object>();
        var screenObj = GetActiveScreenObject();
        if (screenObj != null)
        {
            AddShopItemActions(actions, screenObj, "card", "Cards", "CardOffers", "AvailableCards");
            AddShopItemActions(actions, screenObj, "relic", "Relics", "RelicOffers", "AvailableRelics");
            AddShopItemActions(actions, screenObj, "potion", "Potions", "PotionOffers", "AvailablePotions");
        }

        actions.Add(new { action = "shop_proceed" });
        return actions;
    }

    private static List<object> GetRestActionDescriptors()
    {
        return new List<object>
        {
            new { action = "rest_option", choice = "rest" },
            new { action = "rest_option", choice = "smith" },
            new { action = "rest_proceed" },
        };
    }

    private static List<object> GetTreasureActionDescriptors()
    {
        var actions = new List<object>();
        var screenObj = GetActiveScreenObject();
        var treasures = GetItemsFromMembers(screenObj, "Rewards", "Treasure", "Contents", "Choices", "Options", "Relics");
        if (treasures.Count == 0)
            treasures = GetItemsFromMethods(screenObj, "GetRewards", "GetContents", "GetChoices");

        for (int i = 0; i < treasures.Count; i++)
        {
            actions.Add(new
            {
                action = "treasure_pick",
                treasure_index = i,
                label = GetReadableLabel(treasures[i]),
                treasure_type = treasures[i].GetType().Name,
            });
        }

        actions.Add(new { action = "treasure_proceed" });
        return actions;
    }

    private static List<object> GetCardSelectionActionDescriptors()
    {
        var actions = new List<object>();
        var screenObj = GetActiveScreenObject();
        var cards = GetItemsFromMembers(screenObj, "Cards", "CardChoices", "Choices", "SelectableCards", "Options");
        if (cards.Count == 0)
            cards = GetItemsFromMethods(screenObj, "GetCards", "GetChoices");

        for (int i = 0; i < cards.Count; i++)
        {
            actions.Add(new
            {
                action = "card_select",
                card_index = i,
                label = GetReadableLabel(cards[i]),
                card_type = cards[i].GetType().Name,
            });
        }

        actions.Add(new { action = "card_confirm" });
        actions.Add(new { action = "card_skip" });
        return actions;
    }

    // ─── Reflection Helpers ────────────────────────────────────────────────

    private static object? GetCurrentEventObject()
    {
        var state = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
        if (state?.CurrentRoom == null)
            return null;

        var room = state.CurrentRoom;
        var direct = GetMemberValue(room, "Event") ?? GetMemberValue(room, "CurrentEvent");
        if (direct != null)
            return direct;

        var fields = room.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return fields.FirstOrDefault(field => field.FieldType.Name.Contains("Event", StringComparison.OrdinalIgnoreCase))?.GetValue(room);
    }

    private static object? GetActiveScreenObject()
        => ScreenDetector.TryGetActiveScreenObject(out var screenObject, out _) ? screenObject : null;

    private static object DescribeObjectShape(object? target)
    {
        if (target == null)
            return new { present = false };

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var members = target.GetType()
            .GetMembers(flags)
            .Where(member => member.MemberType is MemberTypes.Property or MemberTypes.Field or MemberTypes.Method)
            .Select(member => member.Name)
            .Distinct()
            .OrderBy(name => name)
            .Take(40)
            .ToList();

        return new
        {
            present = true,
            type = target.GetType().FullName ?? target.GetType().Name,
            sample_members = members,
        };
    }

    private static List<object> GetItemsFromMembers(object? target, params string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            var items = ToObjectList(GetMemberValue(target, memberName));
            if (items.Count > 0)
                return items;
        }

        return new List<object>();
    }

    private static List<object> GetItemsFromMethods(object? target, params string[] methodNames)
    {
        if (target == null)
            return new List<object>();

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var methodName in methodNames)
        {
            var method = target.GetType().GetMethods(flags)
                .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
                    && m.GetParameters().Length == 0);
            if (method == null)
                continue;

            try
            {
                var items = ToObjectList(method.Invoke(target, null));
                if (items.Count > 0)
                    return items;
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"GetItemsFromMethods {methodName} failed: {ex.GetBaseException().Message}");
            }
        }

        return new List<object>();
    }

    private static object? GetMemberValue(object? target, string memberName)
    {
        if (target == null)
            return null;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        try
        {
            var property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target);

            var field = target.GetType().GetField(memberName, flags);
            if (field != null)
                return field.GetValue(target);
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"GetMemberValue {memberName} failed: {ex.GetBaseException().Message}");
        }

        return null;
    }

    private static List<object> ToObjectList(object? value)
    {
        if (value == null)
            return new List<object>();

        if (value is string s)
            return string.IsNullOrWhiteSpace(s) ? new List<object>() : new List<object> { s };

        if (value is IEnumerable enumerable)
        {
            var items = new List<object>();
            foreach (var item in enumerable)
            {
                if (item != null)
                    items.Add(item);
            }

            return items;
        }

        return new List<object> { value };
    }

    private static string GetReadableLabel(object? value)
    {
        if (value == null)
            return "unknown";

        if (value is string s)
            return s;

        foreach (var memberName in new[] { "DisplayName", "Name", "Title", "Label", "Text", "Description", "Tooltip", "Id" })
        {
            var memberValue = GetMemberValue(value, memberName);
            if (memberValue is string text && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        var nestedCard = GetMemberValue(value, "Card");
        if (nestedCard != null && !ReferenceEquals(nestedCard, value))
            return GetReadableLabel(nestedCard);

        return value.GetType().Name;
    }

    private static int? GetNumericMember(object? value, params string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            var memberValue = GetMemberValue(value, memberName);
            if (memberValue == null)
                continue;

            try
            {
                return Convert.ToInt32(memberValue);
            }
            catch (Exception)
            {
                // Expected: memberValue may not be convertible to int — try next candidate
            }
        }

        return null;
    }

    private static bool TryInvokeMethod(object? target, IEnumerable<string> candidateNames, object?[] args, out string? invokedMethod)
    {
        invokedMethod = null;
        if (target == null)
            return false;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var methods = target.GetType().GetMethods(flags);

        foreach (var candidateName in candidateNames)
        {
            foreach (var method in methods.Where(m => string.Equals(m.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                    continue;

                var convertedArgs = new object?[args.Length];
                var isCompatible = true;
                for (int i = 0; i < args.Length; i++)
                {
                    if (!TryConvertArgument(parameters[i].ParameterType, args[i], out convertedArgs[i]))
                    {
                        isCompatible = false;
                        break;
                    }
                }

                if (!isCompatible)
                    continue;

                try
                {
                    method.Invoke(target, convertedArgs);
                    invokedMethod = method.Name;
                    return true;
                }
                catch (Exception ex)
                {
                    ModEntry.WriteLog($"Invoke {method.Name} failed: {ex.GetBaseException().Message}");
                }
            }
        }

        return false;
    }

    private static bool TryConvertArgument(Type parameterType, object? arg, out object? convertedArg)
    {
        convertedArg = null;

        if (arg == null)
        {
            if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null)
                return true;

            return false;
        }

        var normalizedType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        var argType = arg.GetType();

        if (normalizedType.IsAssignableFrom(argType) || normalizedType.IsInstanceOfType(arg))
        {
            convertedArg = arg;
            return true;
        }

        if (normalizedType.IsEnum && arg is string s && Enum.TryParse(normalizedType, s, true, out var enumValue))
        {
            convertedArg = enumValue;
            return true;
        }

        if (normalizedType == typeof(string))
        {
            convertedArg = arg.ToString();
            return true;
        }

        if (normalizedType == typeof(int) && arg is int i)
        {
            convertedArg = i;
            return true;
        }

        if (normalizedType == typeof(bool) && arg is bool b)
        {
            convertedArg = b;
            return true;
        }

        if (normalizedType.IsArray && arg is Array array)
        {
            var elementType = normalizedType.GetElementType();
            if (elementType != null && array.GetType().GetElementType() != null && elementType.IsAssignableFrom(array.GetType().GetElementType()!))
            {
                convertedArg = arg;
                return true;
            }
        }

        if (typeof(IEnumerable).IsAssignableFrom(normalizedType) && arg is IEnumerable)
        {
            convertedArg = arg;
            return true;
        }

        return false;
    }

    private static object ExecuteIndexedScreenInteraction(
        string expectedScreen,
        string actionName,
        int index,
        string[] collectionMembers,
        string[] collectionMethods,
        string[] screenMethods,
        string[] itemMethods)
    {
        var screen = ScreenDetector.GetCurrentScreen();
        if (screen != expectedScreen)
            return new { error = $"Not on {expectedScreen} screen (current screen: {screen})", action = actionName };

        var screenObj = GetActiveScreenObject();
        if (screenObj == null)
            return new { error = $"No active screen object for {expectedScreen}", action = actionName };

        if (TryInvokeMethod(screenObj, screenMethods, [index], out var invokedMethod))
        {
            return new { success = true, action = actionName, index, invoked = invokedMethod };
        }

        var items = GetItemsFromMembers(screenObj, collectionMembers);
        if (items.Count == 0)
            items = GetItemsFromMethods(screenObj, collectionMethods);

        if (index < 0 || index >= items.Count)
            return new { error = $"Index {index} out of range", action = actionName, item_count = items.Count };

        var item = items[index];
        if (TryInvokeMethod(screenObj, screenMethods, [item], out invokedMethod)
            || TryInvokeMethod(screenObj, screenMethods, [new[] { index }], out invokedMethod)
            || TryInvokeMethod(item, itemMethods, Array.Empty<object?>(), out invokedMethod))
        {
            ModEntry.WriteLog($"[{actionName}] index={index} via {invokedMethod}");
            return new
            {
                success = true,
                action = actionName,
                index,
                label = GetReadableLabel(item),
                invoked = invokedMethod,
            };
        }

        return new
        {
            error = $"Unable to execute {actionName} for index {index}",
            action = actionName,
            item_type = item.GetType().Name,
        };
    }

    // ─── Hot Swap Patches ───────────────────────────────────────────────────

    private static object HotSwapPatches(JsonElement root)
    {
        try
        {
            string? dllPath = null;
            if (root.TryGetProperty("params", out var p) && p.TryGetProperty("dll_path", out var dp))
                dllPath = dp.GetString();

            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
                return new { error = $"DLL not found: {dllPath}" };

            var harmony = ModEntry.GetHarmony();
            if (harmony == null)
                return new { error = "Harmony instance not available" };

            // Unpatch everything from this mod
            harmony.UnpatchAll(harmony.Id);
            ModEntry.WriteLog($"[HotSwap] Unpatched all from {harmony.Id}");
            EventTracker.Record("hot_swap", $"Unpatched all from {harmony.Id}");

            // Load new assembly
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
            ModEntry.WriteLog($"[HotSwap] Loaded assembly: {assembly.FullName}");

            // Re-apply patches from the new assembly
            harmony.PatchAll(assembly);
            ModEntry.WriteLog($"[HotSwap] Re-patched from {assembly.FullName}");
            EventTracker.Record("hot_swap", $"Re-patched from {assembly.FullName}");

            var patchedMethods = Harmony.GetAllPatchedMethods().ToList();
            var ownPatches = patchedMethods
                .Select(m => Harmony.GetPatchInfo(m))
                .Where(info => info != null)
                .Select(info => info!.Prefixes.Count(pa => pa.owner == harmony.Id)
                              + info.Postfixes.Count(pa => pa.owner == harmony.Id)
                              + info.Transpilers.Count(pa => pa.owner == harmony.Id))
                .Sum();

            return new { success = true, dll_path = dllPath, assembly_name = assembly.FullName, patch_count = ownPatches };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"[HotSwap] Error: {ex}");
            ExceptionMonitor.Record(ex, "HotSwapPatches");
            return new { error = ex.Message };
        }
    }

    // ─── Exception Monitor ──────────────────────────────────────────────────

    private static object GetExceptions(JsonElement root)
    {
        int maxCount = 20;
        int sinceId = 0;
        if (root.TryGetProperty("params", out var p))
        {
            if (p.TryGetProperty("max_count", out var mc)) maxCount = mc.GetInt32();
            if (p.TryGetProperty("since_id", out var si)) sinceId = si.GetInt32();
        }

        var exceptions = ExceptionMonitor.GetRecent(maxCount, sinceId);
        return new
        {
            count = exceptions.Count,
            exceptions = exceptions.Select(e => new
            {
                id = e.Id,
                timestamp = e.Timestamp.ToString("HH:mm:ss.fff"),
                type = e.Type,
                message = e.Message,
                stack_trace = e.StackTrace.Length > 500 ? e.StackTrace[..500] + "..." : e.StackTrace,
                source = e.Source,
            }).ToList(),
        };
    }

    // ─── State Diffing ──────────────────────────────────────────────────────

    private static object GetStateDiff()
    {
        try
        {
            var currentState = CaptureStateForDiff();

            if (_previousState == null)
            {
                _previousState = currentState;
                return new { first_call = true, message = "State baseline captured. Call again to see changes.", state = currentState };
            }

            var diff = new Dictionary<string, object?>();
            foreach (var key in currentState.Keys.Union(_previousState.Keys))
            {
                var current = currentState.GetValueOrDefault(key);
                var previous = _previousState.GetValueOrDefault(key);
                var currentStr = JsonSerializer.Serialize(current, JsonOpts);
                var previousStr = JsonSerializer.Serialize(previous, JsonOpts);

                if (currentStr != previousStr)
                    diff[key] = new { previous, current };
            }

            _previousState = currentState;

            return new
            {
                has_changes = diff.Count > 0,
                changed_field_count = diff.Count,
                changes = diff,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static Dictionary<string, object?> CaptureStateForDiff()
    {
        var snapshot = new Dictionary<string, object?>();

        snapshot["screen"] = ScreenDetector.GetCurrentScreen();
        snapshot["run_in_progress"] = RunManager.Instance.IsInProgress;

        if (RunManager.Instance.IsInProgress)
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state != null)
            {
                snapshot["floor"] = state.TotalFloor;
                snapshot["act"] = state.CurrentActIndex + 1;

                foreach (var p in state.Players)
                {
                    var prefix = $"player_{p.NetId}";
                    snapshot[$"{prefix}_hp"] = p.Creature?.CurrentHp;
                    snapshot[$"{prefix}_max_hp"] = p.Creature?.MaxHp;
                    snapshot[$"{prefix}_gold"] = p.Gold;
                    snapshot[$"{prefix}_deck_size"] = p.Deck?.Cards.Count;
                    snapshot[$"{prefix}_relic_count"] = p.Relics?.Count;
                }
            }
        }

        var cm = CombatManager.Instance;
        snapshot["in_combat"] = cm?.IsInProgress ?? false;
        if (cm?.IsInProgress == true)
        {
            snapshot["is_player_turn"] = cm.IsPlayPhase;
            var cs = cm.DebugOnlyGetState();
            if (cs != null)
            {
                snapshot["round"] = cs.RoundNumber;
                int ei = 0;
                foreach (var enemy in cs.Enemies)
                {
                    snapshot[$"enemy_{ei}_name"] = enemy.Monster?.GetType().Name;
                    snapshot[$"enemy_{ei}_hp"] = enemy.CurrentHp;
                    snapshot[$"enemy_{ei}_block"] = enemy.Block;
                    snapshot[$"enemy_{ei}_alive"] = enemy.IsAlive;
                    ei++;
                }
                snapshot["enemy_count"] = ei;

                var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
                if (player?.PlayerCombatState != null)
                {
                    var pcs = player.PlayerCombatState;
                    snapshot["energy"] = pcs.Energy;
                    snapshot["hand_size"] = pcs.Hand?.Cards.Count;
                    snapshot["draw_pile_size"] = pcs.DrawPile?.Cards.Count;
                    snapshot["discard_pile_size"] = pcs.DiscardPile?.Cards.Count;
                    snapshot["exhaust_pile_size"] = pcs.ExhaustPile?.Cards.Count;
                    snapshot["hand_cards"] = pcs.Hand?.Cards.Select(c => c.GetType().Name).ToList();

                    var creature = player.Creature;
                    if (creature != null)
                    {
                        snapshot["player_block"] = creature.Block;
                        snapshot["player_powers"] = creature.Powers.Select(pw => $"{pw.GetType().Name}:{pw.Amount}").ToList();
                    }
                }
            }
        }

        return snapshot;
    }

    // ─── Screenshot Capture ─────────────────────────────────────────────────

    private static object CaptureScreenshot(JsonElement root)
    {
        try
        {
            string savePath = "";
            if (root.TryGetProperty("params", out var p) && p.TryGetProperty("save_path", out var sp))
                savePath = sp.GetString() ?? "";

            if (string.IsNullOrWhiteSpace(savePath))
            {
                var dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "MCPTest", "screenshots");
                Directory.CreateDirectory(dir);
                savePath = Path.Combine(dir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            }

            var sceneTree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
            if (sceneTree == null)
                return new { error = "Could not access SceneTree" };

            var image = sceneTree.Root.GetViewport().GetTexture().GetImage();
            image.SavePng(savePath);

            ModEntry.WriteLog($"[Screenshot] Saved to {savePath}");
            EventTracker.Record("screenshot", savePath);
            return new { success = true, path = savePath, width = image.GetWidth(), height = image.GetHeight() };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Event Tracker ──────────────────────────────────────────────────────

    private static object GetEvents(JsonElement root)
    {
        int sinceId = 0;
        int maxCount = 100;
        if (root.TryGetProperty("params", out var p))
        {
            if (p.TryGetProperty("since_id", out var si)) sinceId = si.GetInt32();
            if (p.TryGetProperty("max_count", out var mc)) maxCount = mc.GetInt32();
        }

        var events = EventTracker.GetSince(sinceId, maxCount);
        return new
        {
            latest_id = EventTracker.LatestId,
            count = events.Count,
            events = events.Select(e => new
            {
                id = e.Id,
                timestamp = e.Timestamp.ToString("HH:mm:ss.fff"),
                type = e.Type,
                detail = e.Detail,
                data = e.Data,
            }).ToList(),
        };
    }

    // ─── State Snapshots ────────────────────────────────────────────────────

    private static object SaveSnapshot(JsonElement root)
    {
        string name = "default";
        if (root.TryGetProperty("params", out var p) && p.TryGetProperty("name", out var np))
            name = np.GetString() ?? "default";

        var snapshot = CaptureStateForDiff();
        _snapshots[name] = snapshot;
        ModEntry.WriteLog($"[Snapshot] Saved '{name}' with {snapshot.Count} fields");
        EventTracker.Record("snapshot_saved", name);
        return new { success = true, name, field_count = snapshot.Count, available = _snapshots.Keys.ToList() };
    }

    private static object RestoreSnapshot(JsonElement root)
    {
        string name = "default";
        if (root.TryGetProperty("params", out var p) && p.TryGetProperty("name", out var np))
            name = np.GetString() ?? "default";

        if (!_snapshots.TryGetValue(name, out var snapshot))
            return new { error = $"No snapshot named '{name}'. Available: {string.Join(", ", _snapshots.Keys)}" };

        // Restore via console commands
        var commands = new List<string>();
        if (snapshot.TryGetValue("player_0_hp", out var hp) && hp is int hpVal)
            commands.Add($"heal {hpVal}");
        if (snapshot.TryGetValue("player_0_gold", out var gold) && gold is int goldVal)
            commands.Add($"gold {goldVal}");
        if (snapshot.TryGetValue("energy", out var energy) && energy is int energyVal)
        {
            for (int i = 0; i < energyVal; i++)
                commands.Add("energy");
        }

        ApplyConsoleCommands(commands, "restore_snapshot");

        ModEntry.WriteLog($"[Snapshot] Restored '{name}' with {commands.Count} commands");
        EventTracker.Record("snapshot_restored", name);
        return new { success = true, name, applied_commands = commands.Count, commands };
    }

    // ─── Game Speed Control ─────────────────────────────────────────────────

    private static object SetGameSpeed(JsonElement root)
    {
        try
        {
            float speed = 1.0f;
            if (root.TryGetProperty("params", out var p) && p.TryGetProperty("speed", out var sp))
                speed = (float)sp.GetDouble();

            speed = Math.Clamp(speed, 0.1f, 20.0f);
            Godot.Engine.TimeScale = speed;

            ModEntry.WriteLog($"[GameSpeed] Set to {speed}x");
            EventTracker.Record("game_speed", $"{speed}x");
            return new { success = true, speed = (double)Godot.Engine.TimeScale };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Restart Run (same fixtures) ────────────────────────────────────────

    private static object RestartRun()
    {
        try
        {
            if (_lastRunFixtureParams == null)
                return new { error = "No previous run to restart. Use start_run first." };

            // If a run is in progress, we can't start a new one directly
            if (RunManager.Instance.IsInProgress)
                return new { error = "A run is already in progress. End or abandon it first." };

            var character = _lastRunFixtureParams.GetValueOrDefault("character")?.ToString() ?? "Ironclad";
            var ascension = _lastRunFixtureParams.GetValueOrDefault("ascension") is int asc ? asc : 0;
            var seed = _lastRunFixtureParams.GetValueOrDefault("seed")?.ToString() ?? DateTime.Now.Ticks.ToString();

            ModEntry.WriteLog($"[RestartRun] Restarting with character={character} asc={ascension} seed={seed}");
            EventTracker.Record("restart_run", $"{character} asc={ascension}");

            // Build a minimal root element to reuse StartRun
            var json = JsonSerializer.Serialize(new
            {
                method = "start_run",
                @params = _lastRunFixtureParams,
                id = 0,
            });
            using var doc = JsonDocument.Parse(json);
            return StartRun(doc.RootElement);
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Console / Fixture Helpers ─────────────────────────────────────────

    private static bool TryExecuteConsoleCommand(string command)
    {
        EnsureConsoleAccess();
        if (_processCommandMethod == null || _devConsole == null)
            return false;

        try
        {
            _processCommandMethod.Invoke(_devConsole, new object[] { command });
            return true;
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"Console invoke failed for '{command}': {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static void ApplyConsoleCommands(IEnumerable<string> commands, string reason)
    {
        foreach (var command in commands.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (TryExecuteConsoleCommand(command))
                ModEntry.WriteLog($"[{reason}] {command}");
        }
    }

    private static List<string> BuildFixtureCommands(JsonElement source)
    {
        var commands = new List<string>();

        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("fixture", out var nestedFixture) && nestedFixture.ValueKind == JsonValueKind.Object)
            commands.AddRange(BuildFixtureCommands(nestedFixture));

        if (source.ValueKind != JsonValueKind.Object)
            return commands;

        if (source.TryGetProperty("hp", out var hp) && hp.ValueKind == JsonValueKind.Number)
            commands.Add($"heal {hp.GetInt32()}");
        if (source.TryGetProperty("gold", out var gold) && gold.ValueKind == JsonValueKind.Number)
            commands.Add($"gold {gold.GetInt32()}");
        if (source.TryGetProperty("draw_cards", out var drawCount) && drawCount.ValueKind == JsonValueKind.Number)
            commands.Add($"draw {drawCount.GetInt32()}");
        if (source.TryGetProperty("fight", out var fight) && fight.ValueKind == JsonValueKind.String)
            commands.Add($"fight {fight.GetString()}");
        if (source.TryGetProperty("godmode", out var godmode) && godmode.ValueKind == JsonValueKind.True)
            commands.Add("godmode");
        if (source.TryGetProperty("energy", out var energy) && energy.ValueKind == JsonValueKind.Number)
        {
            for (int i = 0; i < Math.Max(0, energy.GetInt32()); i++)
                commands.Add("energy");
        }

        AppendStringCommands(commands, source, "add_relic", "relic add {0}");
        AppendStringCommands(commands, source, "add_relics", "relic add {0}");
        AppendStringCommands(commands, source, "add_card", "card {0}");
        AppendStringCommands(commands, source, "add_cards", "card {0}");
        AppendStringCommands(commands, source, "console_command", "{0}");
        AppendStringCommands(commands, source, "console_commands", "{0}");

        if (source.TryGetProperty("add_power", out var power) && power.ValueKind == JsonValueKind.Object)
            commands.Add(BuildPowerCommand(power));
        if (source.TryGetProperty("add_powers", out var powers) && powers.ValueKind == JsonValueKind.Array)
        {
            foreach (var powerEntry in powers.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object))
                commands.Add(BuildPowerCommand(powerEntry));
        }

        return commands.Where(command => !string.IsNullOrWhiteSpace(command)).ToList();
    }

    private static void AppendStringCommands(List<string> commands, JsonElement source, string propertyName, string format)
    {
        if (!source.TryGetProperty(propertyName, out var propertyValue))
            return;

        if (propertyValue.ValueKind == JsonValueKind.String)
        {
            var text = propertyValue.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                commands.Add(string.Format(format, text));
            return;
        }

        if (propertyValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in propertyValue.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                    continue;

                var text = entry.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    commands.Add(string.Format(format, text));
            }
        }
    }

    private static string BuildPowerCommand(JsonElement power)
    {
        var name = power.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var stacks = power.TryGetProperty("stacks", out var stacksProp) ? stacksProp.GetInt32() : 1;
        var target = power.TryGetProperty("target", out var targetProp) ? targetProp.GetInt32() : 0;
        return $"power {name} {stacks} {target}";
    }

    private static string MapShopBuyAction(JsonElement p)
    {
        if (p.TryGetProperty("shop_action", out var explicitAction) && explicitAction.ValueKind == JsonValueKind.String)
            return explicitAction.GetString() ?? "buy_card";

        var itemType = p.TryGetProperty("item_type", out var itemTypeProp)
            ? (itemTypeProp.GetString() ?? "card").Trim().ToLowerInvariant()
            : "card";

        return itemType switch
        {
            "card" => "buy_card",
            "relic" => "buy_relic",
            "potion" => "buy_potion",
            "remove" or "purge" => "remove_card",
            _ => "buy_card",
        };
    }

    private static void AddShopItemActions(List<object> actions, object screenObj, string itemType, params string[] memberNames)
    {
        var items = GetItemsFromMembers(screenObj, memberNames);
        for (int i = 0; i < items.Count; i++)
        {
            actions.Add(new
            {
                action = "shop_buy",
                item_type = itemType,
                index = i,
                label = GetReadableLabel(items[i]),
                cost = GetNumericMember(items[i], "Price", "Cost", "GoldCost"),
            });
        }
    }

    private static List<string> ReadBridgeLogLines(int lines, string? contains)
    {
        var logPath = ModEntry.GetLogPath();
        if (!File.Exists(logPath))
            return new List<string>();

        var allLines = File.ReadAllLines(logPath)
            .Where(line => string.IsNullOrWhiteSpace(contains) || line.Contains(contains, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return allLines.Skip(Math.Max(0, allLines.Count - lines)).ToList();
    }

    private static string ToPascalCase(string value)
    {
        var parts = value.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static bool HasError(object? value)
        => value?.GetType().GetProperty("error")?.GetValue(value) != null;

    // ─── Breakpoints & Stepping ─────────────────────────────────────────────────

    private static object DebugPause()
    {
        // If already paused (e.g., at a hook breakpoint), just return current context
        if (BreakpointManager.IsPaused)
        {
            return new
            {
                success = true,
                paused = true,
                already_paused = true,
                context = FormatBreakpointContext(BreakpointManager.GetCurrentContext()),
            };
        }

        // Dispatch to main thread for safe game state capture.
        // This is safe because we checked !IsPaused above (main thread isn't blocked).
        try
        {
            return MainThreadDispatcher.Invoke<object>(() =>
            {
                BreakpointManager.PauseActions();
                return new
                {
                    success = true,
                    paused = true,
                    context = FormatBreakpointContext(BreakpointManager.GetCurrentContext()),
                };
            });
        }
        catch (TimeoutException)
        {
            // Main thread is busy — pause without state capture
            BreakpointManager.PauseActions();
            return new
            {
                success = true,
                paused = true,
                context = (object?)null,
                note = "Main thread was busy; pause set but state not captured. Use debug_get_context to inspect.",
            };
        }
    }

    // NOTE: Resume does NOT use MainThreadDispatcher.Invoke() because
    // hook-level breakpoints block the main thread with ManualResetEventSlim.
    // Calling Invoke() would deadlock. Instead, resume runs directly on the
    // TCP handler thread and signals the blocked main thread to continue.
    private static object DebugResume()
    {
        BreakpointManager.Resume();
        return new { success = true, paused = false };
    }

    private static object DebugStep(JsonElement root)
    {
        string mode = "action";
        if (root.TryGetProperty("params", out var p) && p.TryGetProperty("mode", out var mProp))
            mode = mProp.GetString() ?? "action";

        var stepMode = mode.ToLowerInvariant() switch
        {
            "action" => BreakpointManager.StepMode.Action,
            "turn" => BreakpointManager.StepMode.Turn,
            _ => BreakpointManager.StepMode.Action,
        };

        BreakpointManager.SetStepMode(stepMode);
        BreakpointManager.Step();

        return new
        {
            success = true,
            step_mode = stepMode.ToString(),
            message = $"Stepping in {stepMode} mode — will pause at next {mode}",
        };
    }

    private static object DebugSetBreakpoint(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("params", out var p))
                return new { error = "No parameters provided" };

            string typeStr = "action";
            string target = "";
            string? condition = null;

            if (p.TryGetProperty("type", out var tProp)) typeStr = tProp.GetString() ?? "action";
            if (p.TryGetProperty("target", out var targetProp)) target = targetProp.GetString() ?? "";
            if (p.TryGetProperty("condition", out var cProp)) condition = cProp.GetString();

            if (string.IsNullOrEmpty(target))
                return new { error = "target is required (action type name or hook name)" };

            var bpType = typeStr.ToLowerInvariant() switch
            {
                "action" => BreakpointManager.BreakpointType.Action,
                "hook" => BreakpointManager.BreakpointType.Hook,
                "condition" => BreakpointManager.BreakpointType.Condition,
                _ => BreakpointManager.BreakpointType.Action,
            };

            var bp = BreakpointManager.AddBreakpoint(bpType, target, condition);
            return new
            {
                success = true,
                breakpoint_id = bp.Id,
                type = bpType.ToString(),
                target,
                condition,
                available_hooks = new[]
                {
                    "BeforeCombatStart", "BeforePlayPhaseStart", "BeforeSideTurnStart",
                    "BeforeTurnEnd", "AfterTurnEnd",
                    "BeforeCardPlayed", "AfterCardPlayed",
                    "BeforeDamageReceived", "AfterDamageReceived",
                    "BeforeDeath", "AfterDeath",
                    "BeforePowerAmountChanged", "AfterPowerAmountChanged",
                    "BeforeBlockGained",
                    "BeforeRoomEntered", "AfterRoomEntered",
                    "BeforePotionUsed", "AfterEnergySpent", "BeforeHandDraw",
                },
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object DebugRemoveBreakpoint(JsonElement root)
    {
        int id = 0;
        if (root.TryGetProperty("params", out var p) && p.TryGetProperty("id", out var idProp))
            id = idProp.GetInt32();

        bool removed = BreakpointManager.RemoveBreakpoint(id);
        return new { success = removed, breakpoint_id = id };
    }

    private static object DebugListBreakpoints()
    {
        var bps = BreakpointManager.ListBreakpoints();
        return new
        {
            paused = BreakpointManager.IsPaused,
            step_mode = BreakpointManager.GetStepMode().ToString(),
            breakpoints = bps.Select(bp => new
            {
                id = bp.Id,
                type = bp.Type.ToString(),
                target = bp.Target,
                enabled = bp.Enabled,
                hit_count = bp.HitCount,
                condition = bp.Condition,
            }).ToList(),
        };
    }

    private static object DebugClearBreakpoints()
    {
        BreakpointManager.ClearAllBreakpoints();
        return new { success = true, message = "All breakpoints and step mode cleared" };
    }

    private static object DebugGetContext()
    {
        var ctx = BreakpointManager.GetCurrentContext();
        return new
        {
            paused = BreakpointManager.IsPaused,
            step_mode = BreakpointManager.GetStepMode().ToString(),
            context = FormatBreakpointContext(ctx),
        };
    }

    private static object? FormatBreakpointContext(BreakpointManager.BreakpointContext? ctx)
    {
        if (ctx == null) return null;
        return new
        {
            location = ctx.Location,
            reason = ctx.Reason,
            breakpoint_id = ctx.BreakpointId,
            action_type = ctx.ActionType,
            action_detail = ctx.ActionDetail,
            hook_name = ctx.HookName,
            timestamp = ctx.Timestamp.ToString("HH:mm:ss.fff"),
            game_state = ctx.GameState,
        };
    }

    // ─── Game Log & Debug ─────────────────────────────────────────────────────

    private static object GetGameLog(JsonElement root)
    {
        try
        {
            int maxCount = 100;
            int sinceId = 0;
            string? levelFilter = null;
            string? contains = null;

            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("max_count", out var mc)) maxCount = Math.Clamp(mc.GetInt32(), 1, 500);
                if (p.TryGetProperty("since_id", out var si)) sinceId = si.GetInt32();
                if (p.TryGetProperty("level", out var lf)) levelFilter = lf.GetString();
                if (p.TryGetProperty("contains", out var ct)) contains = ct.GetString();
            }

            var entries = GameLogCapture.GetRecent(maxCount, sinceId, levelFilter, contains);
            return new
            {
                entries = entries.Select(e => new
                {
                    id = e.Id,
                    timestamp = e.Timestamp.ToString("HH:mm:ss.fff"),
                    level = e.Level,
                    message = e.Message,
                }).ToList(),
                count = entries.Count,
                latest_id = GameLogCapture.LatestId,
                capture_level = GameLogCapture.MinCaptureLevel.ToString(),
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object SetLogLevel(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("params", out var p))
                return new { error = "No parameters provided" };

            var results = new List<string>();

            // Set game log level per type: e.g. {"type": "Actions", "level": "Debug"}
            if (p.TryGetProperty("type", out var typeProp) && p.TryGetProperty("level", out var levelProp))
            {
                var typeStr = typeProp.GetString() ?? "";
                var levelStr = levelProp.GetString() ?? "";

                if (Enum.TryParse<MegaCrit.Sts2.Core.Logging.LogType>(typeStr, true, out var logType)
                    && Enum.TryParse<MegaCrit.Sts2.Core.Logging.LogLevel>(levelStr, true, out var logLevel))
                {
                    MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[logType] = logLevel;
                    results.Add($"Game log {logType} → {logLevel}");
                }
                else
                {
                    return new
                    {
                        error = $"Invalid type '{typeStr}' or level '{levelStr}'",
                        valid_types = Enum.GetNames<MegaCrit.Sts2.Core.Logging.LogType>(),
                        valid_levels = Enum.GetNames<MegaCrit.Sts2.Core.Logging.LogLevel>(),
                    };
                }
            }

            // Set global log level: {"global_level": "Debug"}
            if (p.TryGetProperty("global_level", out var globalProp))
            {
                var globalStr = globalProp.GetString() ?? "";
                if (Enum.TryParse<MegaCrit.Sts2.Core.Logging.LogLevel>(globalStr, true, out var globalLevel))
                {
                    MegaCrit.Sts2.Core.Logging.Logger.GlobalLogLevel = globalLevel;
                    results.Add($"Global log level → {globalLevel}");
                }
                else
                {
                    return new
                    {
                        error = $"Invalid global level '{globalStr}'",
                        valid_levels = Enum.GetNames<MegaCrit.Sts2.Core.Logging.LogLevel>(),
                    };
                }
            }

            // Set capture level for our ring buffer: {"capture_level": "VeryDebug"}
            if (p.TryGetProperty("capture_level", out var captureProp))
            {
                var captureStr = captureProp.GetString() ?? "";
                if (Enum.TryParse<MegaCrit.Sts2.Core.Logging.LogLevel>(captureStr, true, out var captureLevel))
                {
                    GameLogCapture.SetMinLevel(captureLevel);
                    results.Add($"Capture level → {captureLevel}");
                }
            }

            if (results.Count == 0)
                return new { error = "No valid log settings provided. Use type+level, global_level, or capture_level." };

            ModEntry.WriteLog($"[SetLogLevel] {string.Join(", ", results)}");
            return new { success = true, applied = results };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object GetLogLevels()
    {
        try
        {
            var levels = new Dictionary<string, string>();
            foreach (var kvp in MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap)
                levels[kvp.Key.ToString()] = kvp.Value.ToString();

            return new
            {
                global_level = MegaCrit.Sts2.Core.Logging.Logger.GlobalLogLevel.ToString(),
                type_levels = levels,
                capture_level = GameLogCapture.MinCaptureLevel.ToString(),
                valid_types = Enum.GetNames<MegaCrit.Sts2.Core.Logging.LogType>(),
                valid_levels = Enum.GetNames<MegaCrit.Sts2.Core.Logging.LogLevel>(),
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object ClearExceptions()
    {
        ExceptionMonitor.Clear();
        ModEntry.WriteLog("[ClearExceptions] Exception buffer cleared");
        return new { success = true, message = "Exception buffer cleared" };
    }

    private static object ClearEvents()
    {
        EventTracker.Clear();
        ModEntry.WriteLog("[ClearEvents] Event buffer cleared");
        return new { success = true, message = "Event buffer cleared" };
    }

    // ─── AutoSlay Integration ────────────────────────────────────────────────

    private static object? _autoSlayerInstance;
    private static Type? _autoSlayerType;
    private static Type? _autoSlayConfigType;
    private static System.Threading.CancellationTokenSource? _autoSlayCts;
    private static DateTime _autoSlayStartTime;
    private static string _autoSlayCharacter = "";
    private static string _autoSlaySeed = "";
    private static bool _autoSlayRunning;
    private static string? _autoSlayError;
    private static int _autoSlayRunsCompleted;
    private static int _autoSlayRunsRequested;

    // Custom config overrides (applied via reflection before each run)
    private static int? _autoSlayCfgRunTimeout;
    private static int? _autoSlayCfgRoomTimeout;
    private static int? _autoSlayCfgScreenTimeout;
    private static int? _autoSlayCfgPollingInterval;
    private static int? _autoSlayCfgWatchdogTimeout;
    private static int? _autoSlayCfgMaxFloor;

    private static bool EnsureAutoSlayTypes()
    {
        if (_autoSlayerType != null) return true;

        _autoSlayerType = Type.GetType("MegaCrit.Sts2.Core.AutoSlay.AutoSlayer, sts2");
        _autoSlayConfigType = Type.GetType("MegaCrit.Sts2.Core.AutoSlay.AutoSlayConfig, sts2");

        if (_autoSlayerType == null)
        {
            ModEntry.WriteLog("AutoSlay: AutoSlayer type not found in game assembly");
            return false;
        }
        ModEntry.WriteLog($"AutoSlay: Found AutoSlayer type: {_autoSlayerType.FullName}");
        return true;
    }

    private static object AutoSlayStart(JsonElement root)
    {
        try
        {
            if (!EnsureAutoSlayTypes())
                return new { error = "AutoSlay types not found in game assembly. The game may not include AutoSlay in this version." };

            // Parse params
            string character = "Ironclad";
            string seed = "";
            int runs = 1;
            bool loop = false;

            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("character", out var cProp))
                    character = cProp.GetString() ?? "Ironclad";
                if (p.TryGetProperty("seed", out var sProp))
                    seed = sProp.ValueKind == JsonValueKind.String ? (sProp.GetString() ?? "") : sProp.ToString();
                if (p.TryGetProperty("runs", out var rProp))
                    runs = rProp.GetInt32();
                if (p.TryGetProperty("loop", out var lProp))
                    loop = lProp.GetBoolean();
            }

            if (_autoSlayRunning)
                return new { error = "AutoSlay is already running. Call autoslay_stop first." };

            _autoSlayCts = new System.Threading.CancellationTokenSource();
            _autoSlayStartTime = DateTime.Now;
            _autoSlayCharacter = character;
            _autoSlaySeed = seed;
            _autoSlayError = null;
            _autoSlayRunsCompleted = 0;
            _autoSlayRunsRequested = loop ? -1 : runs;
            _autoSlayRunning = true;

            var ct = _autoSlayCts.Token;
            var totalRuns = loop ? -1 : runs;

            // Launch AutoSlay on a background thread
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int runCount = 0;
                    while (totalRuns == -1 || runCount < totalRuns)
                    {
                        if (ct.IsCancellationRequested) break;

                        string runSeed = string.IsNullOrEmpty(seed) ? DateTime.Now.Ticks.ToString() : seed;
                        if (runCount > 0 && !string.IsNullOrEmpty(seed))
                            runSeed = seed + "_" + runCount;

                        ModEntry.WriteLog($"AutoSlay: Starting run {runCount + 1} (seed={runSeed}, character={character})");
                        RunAutoSlayOnce(runSeed, character, ct);
                        _autoSlayRunsCompleted = ++runCount;
                        ModEntry.WriteLog($"AutoSlay: Run {runCount} completed (total={_autoSlayRunsCompleted})");

                        if (ct.IsCancellationRequested) break;

                        // Brief pause between runs
                        if (totalRuns == -1 || runCount < totalRuns)
                            System.Threading.Thread.Sleep(2000);
                    }

                    ModEntry.WriteLog($"AutoSlay: All {runCount} run(s) finished");
                }
                catch (OperationCanceledException)
                {
                    ModEntry.WriteLog("AutoSlay: Cancelled by user");
                }
                catch (Exception ex)
                {
                    _autoSlayError = ex.Message;
                    ModEntry.WriteLog($"AutoSlay: Error: {ex}");
                }
                finally
                {
                    _autoSlayRunning = false;
                }
            });

            return new
            {
                success = true,
                message = loop ? $"AutoSlay looping started (character={character})" : $"AutoSlay started for {runs} run(s)",
                character,
                seed = string.IsNullOrEmpty(seed) ? "random" : seed,
                runs = totalRuns,
                loop,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static void RunAutoSlayOnce(string seed, string character, System.Threading.CancellationToken ct)
    {
        // Use reflection to create and run AutoSlayer
        // The game's AutoSlayer.RunAsync(seed, ct) drives the full game loop
        try
        {
            // Get NGame instance
            var nGameType = Type.GetType("MegaCrit.Sts2.Core.Nodes.NGame, sts2");
            var nGameInstance = nGameType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

            if (nGameInstance == null)
            {
                ModEntry.WriteLog("AutoSlay: NGame.Instance is null, waiting...");
                for (int i = 0; i < 50 && nGameInstance == null; i++)
                {
                    System.Threading.Thread.Sleep(200);
                    nGameInstance = nGameType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (ct.IsCancellationRequested) return;
                }
                if (nGameInstance == null)
                    throw new Exception("NGame.Instance not available after 10s");
            }

            // Apply config overrides if any
            ApplyAutoSlayConfig();

            // Create AutoSlayer instance on the main thread and call Start().
            // Start() internally calls RunAsync with TaskHelper.RunSafely, which
            // uses the game's SynchronizationContext for async coordination.
            var autoSlayer = Activator.CreateInstance(_autoSlayerType!);
            _autoSlayerInstance = autoSlayer;

            var startMethod = _autoSlayerType!.GetMethod("Start", BindingFlags.Public | BindingFlags.Instance);
            if (startMethod == null)
                throw new Exception("AutoSlayer.Start method not found");

            var isActiveProp = _autoSlayerType.GetProperty("IsActive", BindingFlags.Public | BindingFlags.Static);
            if (isActiveProp == null)
                throw new Exception("AutoSlayer.IsActive property not found");

            ModEntry.WriteLog($"AutoSlay: Dispatching Start(seed={seed}) to main thread");

            // Dispatch to main thread — Start() needs Godot's async context
            MainThreadDispatcher.Post(() =>
            {
                try { startMethod.Invoke(autoSlayer, new object?[] { seed, null }); }
                catch (Exception ex)
                {
                    _autoSlayError = ex.InnerException?.Message ?? ex.Message;
                    ModEntry.WriteLog($"AutoSlay: Start dispatch error: {_autoSlayError}");
                    _autoSlayRunning = false;
                }
            });

            // Wait briefly for Start() to execute on main thread
            System.Threading.Thread.Sleep(1000);

            // Poll IsActive until the run finishes or we're cancelled
            while (!ct.IsCancellationRequested)
            {
                var isActive = (bool)(isActiveProp.GetValue(null) ?? false);
                if (!isActive) break;
                System.Threading.Thread.Sleep(500);
            }

            // If cancelled, call Stop()
            if (ct.IsCancellationRequested)
            {
                var stopMethod = _autoSlayerType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance);
                if (stopMethod != null)
                {
                    try { stopMethod.Invoke(autoSlayer, null); }
                    catch { }
                }
            }
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            if (tie.InnerException is OperationCanceledException)
                throw tie.InnerException;
            ModEntry.WriteLog($"AutoSlay: InnerException: {tie.InnerException}");
            throw tie.InnerException;
        }
        finally
        {
            _autoSlayerInstance = null;
        }
    }

    private static void ApplyAutoSlayConfig()
    {
        if (_autoSlayConfigType == null) return;

        try
        {
            // AutoSlayConfig typically has static fields/properties for timeouts
            void TrySetField(string fieldName, int? value)
            {
                if (value == null) return;
                var field = _autoSlayConfigType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
                    ?? _autoSlayConfigType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
                var prop = _autoSlayConfigType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Static)
                    ?? _autoSlayConfigType.GetProperty(fieldName, BindingFlags.NonPublic | BindingFlags.Static);

                if (field != null && !field.IsInitOnly && !field.IsLiteral)
                {
                    if (field.FieldType == typeof(TimeSpan))
                        field.SetValue(null, TimeSpan.FromSeconds(value.Value));
                    else if (field.FieldType == typeof(int))
                        field.SetValue(null, value.Value);
                    ModEntry.WriteLog($"AutoSlay: Set config {fieldName} = {value.Value}");
                }
                else if (prop != null && prop.CanWrite)
                {
                    if (prop.PropertyType == typeof(TimeSpan))
                        prop.SetValue(null, TimeSpan.FromSeconds(value.Value));
                    else if (prop.PropertyType == typeof(int))
                        prop.SetValue(null, value.Value);
                    ModEntry.WriteLog($"AutoSlay: Set config {fieldName} = {value.Value}");
                }
            }

            TrySetField("RunTimeout", _autoSlayCfgRunTimeout);
            TrySetField("runTimeout", _autoSlayCfgRunTimeout);
            TrySetField("DefaultRoomTimeout", _autoSlayCfgRoomTimeout);
            TrySetField("defaultRoomTimeout", _autoSlayCfgRoomTimeout);
            TrySetField("DefaultScreenTimeout", _autoSlayCfgScreenTimeout);
            TrySetField("defaultScreenTimeout", _autoSlayCfgScreenTimeout);
            TrySetField("PollingInterval", _autoSlayCfgPollingInterval);
            TrySetField("pollingInterval", _autoSlayCfgPollingInterval);
            TrySetField("WatchdogTimeout", _autoSlayCfgWatchdogTimeout);
            TrySetField("watchdogTimeout", _autoSlayCfgWatchdogTimeout);
            TrySetField("MaxFloor", _autoSlayCfgMaxFloor);
            TrySetField("maxFloor", _autoSlayCfgMaxFloor);

            // Also log all config fields for diagnostic purposes
            var fields = _autoSlayConfigType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                try { ModEntry.WriteLog($"AutoSlay: Config {f.Name} = {f.GetValue(null)}"); }
                catch (Exception ex) { ModEntry.WriteLog($"AutoSlay: Config read {f.Name} error: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"AutoSlay: Config apply error: {ex.Message}");
        }
    }

    private static object AutoSlayStop()
    {
        try
        {
            if (!_autoSlayRunning)
                return new { success = true, message = "AutoSlay was not running" };

            _autoSlayCts?.Cancel();

            // Also try to stop via the instance if available
            if (_autoSlayerInstance != null && _autoSlayerType != null)
            {
                var stopMethod = _autoSlayerType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance)
                    ?? _autoSlayerType.GetMethod("Cancel", BindingFlags.Public | BindingFlags.Instance);
                if (stopMethod != null)
                {
                    try { stopMethod.Invoke(_autoSlayerInstance, null); }
                    catch (Exception ex) { ModEntry.WriteLog($"AutoSlay: Stop method error: {ex.Message}"); }
                }
            }

            ModEntry.WriteLog("AutoSlay: Stop requested");
            return new
            {
                success = true,
                message = "AutoSlay stop requested",
                runs_completed = _autoSlayRunsCompleted,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object AutoSlayGetStatus()
    {
        try
        {
            var status = new Dictionary<string, object?>
            {
                ["running"] = _autoSlayRunning,
                ["character"] = _autoSlayCharacter,
                ["seed"] = _autoSlaySeed,
                ["runs_completed"] = _autoSlayRunsCompleted,
                ["runs_requested"] = _autoSlayRunsRequested == -1 ? "infinite" : _autoSlayRunsRequested.ToString(),
                ["error"] = _autoSlayError,
            };

            if (_autoSlayRunning)
            {
                var elapsed = DateTime.Now - _autoSlayStartTime;
                status["elapsed_seconds"] = (int)elapsed.TotalSeconds;
                status["elapsed_display"] = elapsed.ToString(@"hh\:mm\:ss");
            }

            // Try to get current game state for context
            try
            {
                status["run_in_progress"] = RunManager.Instance?.IsInProgress ?? false;
                status["in_combat"] = CombatManager.Instance?.IsInProgress ?? false;
                status["screen"] = ScreenDetector.GetCurrentScreen();

                if (RunManager.Instance?.IsInProgress == true)
                {
                    var runState = RunManager.Instance.DebugOnlyGetState();
                    if (runState != null)
                    {
                        status["floor"] = runState.TotalFloor;
                        status["act"] = runState.CurrentActIndex + 1;
                        status["current_room"] = runState.CurrentRoom?.GetType().Name;
                    }
                }
            }
            catch (Exception ex) { ModEntry.WriteLog($"AutoSlay status game state error: {ex.Message}"); }

            // Read recent AutoSlay log entries
            try
            {
                var logLines = ReadBridgeLogLines(20, "AutoSlay");
                status["recent_log"] = logLines;
            }
            catch (Exception ex) { ModEntry.WriteLog($"AutoSlay status log read error: {ex.Message}"); }

            return status;
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object AutoSlayConfigure(JsonElement root)
    {
        try
        {
            if (!EnsureAutoSlayTypes())
                return new { error = "AutoSlay types not found" };

            var applied = new Dictionary<string, object>();

            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("run_timeout_seconds", out var v1))
                    { _autoSlayCfgRunTimeout = v1.GetInt32(); applied["run_timeout_seconds"] = v1.GetInt32(); }
                if (p.TryGetProperty("room_timeout_seconds", out var v2))
                    { _autoSlayCfgRoomTimeout = v2.GetInt32(); applied["room_timeout_seconds"] = v2.GetInt32(); }
                if (p.TryGetProperty("screen_timeout_seconds", out var v3))
                    { _autoSlayCfgScreenTimeout = v3.GetInt32(); applied["screen_timeout_seconds"] = v3.GetInt32(); }
                if (p.TryGetProperty("polling_interval_ms", out var v4))
                    { _autoSlayCfgPollingInterval = v4.GetInt32(); applied["polling_interval_ms"] = v4.GetInt32(); }
                if (p.TryGetProperty("watchdog_timeout_seconds", out var v5))
                    { _autoSlayCfgWatchdogTimeout = v5.GetInt32(); applied["watchdog_timeout_seconds"] = v5.GetInt32(); }
                if (p.TryGetProperty("max_floor", out var v6))
                    { _autoSlayCfgMaxFloor = v6.GetInt32(); applied["max_floor"] = v6.GetInt32(); }
            }

            if (applied.Count == 0)
                return new { error = "No configuration parameters provided" };

            // Read current config for response
            var currentConfig = new Dictionary<string, object?>();
            if (_autoSlayConfigType != null)
            {
                var fields = _autoSlayConfigType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                foreach (var f in fields)
                {
                    try { currentConfig[f.Name] = f.GetValue(null)?.ToString(); }
                    catch (Exception) { /* Reflection read failure — non-critical */ }
                }
            }

            return new
            {
                success = true,
                applied,
                note = "Config will be applied on next AutoSlay start",
                current_game_config = currentConfig,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Menu Navigation (works without window focus) ───────────────────────

    private static object NavigateMenu(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("params", out var p) || !p.TryGetProperty("target", out var targetProp))
                return new { error = "navigate_menu requires params.target (continue, compendium, card_library, new_run, abandon, back)" };

            var target = (targetProp.GetString() ?? "").Trim().ToLowerInvariant();

            // Get NGame instance via reflection
            var nGameType = Type.GetType("MegaCrit.Sts2.Core.Nodes.NGame, sts2");
            var nGameInstance = nGameType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (nGameInstance == null)
                return new { error = "NGame.Instance not available" };

            // Get the MainMenu from NGame
            var mainMenuProp = nGameType!.GetProperty("MainMenu", BindingFlags.Public | BindingFlags.Instance);
            var mainMenu = mainMenuProp?.GetValue(nGameInstance);

            switch (target)
            {
                case "continue":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    // Call the private OnContinueButtonPressed method
                    var method = mainMenu.GetType().GetMethod("OnContinueButtonPressed",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (method == null)
                    {
                        // Try calling OnContinueButtonPressedAsync directly
                        var asyncMethod = mainMenu.GetType().GetMethod("OnContinueButtonPressedAsync",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (asyncMethod == null)
                            return new { error = "Could not find continue method on NMainMenu" };

                        asyncMethod.Invoke(mainMenu, null);
                        ModEntry.WriteLog("[navigate_menu] Invoked OnContinueButtonPressedAsync");
                        return new { success = true, target, invoked = "OnContinueButtonPressedAsync" };
                    }

                    // OnContinueButtonPressed takes an NButton parameter — pass null
                    method.Invoke(mainMenu, new object?[] { null });
                    ModEntry.WriteLog("[navigate_menu] Invoked OnContinueButtonPressed");
                    return new { success = true, target, invoked = "OnContinueButtonPressed" };
                }

                case "compendium":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    // Get SubmenuStack and push NCompendiumSubmenu
                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    var compType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NCompendiumSubmenu, sts2");
                    if (compType == null)
                        return new { error = "Could not find NCompendiumSubmenu type" };

                    var pushMethods = stack.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "PushSubmenuType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var pushMethod = pushMethods.FirstOrDefault();
                    if (pushMethod == null)
                        return new { error = "Could not find PushSubmenuType method" };

                    var genericPush = pushMethod.MakeGenericMethod(compType);
                    genericPush.Invoke(stack, null);
                    ModEntry.WriteLog("[navigate_menu] Pushed NCompendiumSubmenu");
                    return new { success = true, target, invoked = "PushSubmenuType<NCompendiumSubmenu>" };
                }

                case "card_library":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    // Use PushSubmenuType<NCardLibrary>() — simpler, no ambiguity
                    var cardLibType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary.NCardLibrary, sts2")
                        ?? Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NCardLibrary, sts2");
                    if (cardLibType == null)
                        return new { error = "Could not find NCardLibrary type" };

                    // Find PushSubmenuType (generic method)
                    var pushMethods = stack.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "PushSubmenuType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var pushMethod = pushMethods.FirstOrDefault();
                    if (pushMethod == null)
                        return new { error = "Could not find PushSubmenuType method" };

                    var genericPush = pushMethod.MakeGenericMethod(cardLibType);
                    genericPush.Invoke(stack, null);
                    ModEntry.WriteLog("[navigate_menu] PushSubmenuType<NCardLibrary>");
                    return new { success = true, target, invoked = "PushSubmenuType<NCardLibrary>" };
                }

                case "new_run" or "new_game" or "singleplayer":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    // Call SingleplayerButtonPressed
                    var method = mainMenu.GetType().GetMethod("SingleplayerButtonPressed",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (method == null)
                        return new { error = "Could not find SingleplayerButtonPressed" };

                    method.Invoke(mainMenu, new object?[] { null });
                    ModEntry.WriteLog("[navigate_menu] Invoked SingleplayerButtonPressed");
                    return new { success = true, target, invoked = "SingleplayerButtonPressed" };
                }

                case "abandon":
                {
                    if (!RunManager.Instance.IsInProgress)
                        return new { error = "No run in progress to abandon" };

                    RunManager.Instance.Abandon();
                    ModEntry.WriteLog("[navigate_menu] Abandoned run");
                    return new { success = true, target, invoked = "Abandon" };
                }

                case "back":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    var popMethod = stack.GetType().GetMethod("Pop",
                        BindingFlags.Public | BindingFlags.Instance);
                    popMethod?.Invoke(stack, null);
                    ModEntry.WriteLog("[navigate_menu] Popped submenu stack");
                    return new { success = true, target, invoked = "Pop" };
                }

                case "proceed" or "continue_screen" or "dismiss":
                {
                    // Generic proceed — works on game over, death, reward, etc.
                    var screenObj = GetActiveScreenObject();
                    if (screenObj == null)
                        return new { error = "No active screen object" };

                    // Try common proceed/continue patterns
                    if (TryInvokeMethod(screenObj, ["OpenSummaryScreen", "Proceed", "Continue", "Confirm", "Done", "Close", "Leave", "Accept", "Dismiss"], Array.Empty<object?>(), out var invokedMethod))
                    {
                        ModEntry.WriteLog($"[navigate_menu] proceed via {invokedMethod} on {screenObj.GetType().Name}");
                        return new { success = true, target, invoked = invokedMethod, screen_type = screenObj.GetType().Name };
                    }

                    // Try finding and clicking a continue/proceed button
                    if (screenObj is Godot.Node screenNode)
                    {
                        foreach (var btnName in new[] { "%ContinueButton", "%ProceedButton", "%DoneButton", "%CloseButton" })
                        {
                            var btn = screenNode.GetNodeOrNull(btnName);
                            if (btn is BaseButton baseBtn)
                            {
                                baseBtn.EmitSignal("pressed");
                                ModEntry.WriteLog($"[navigate_menu] proceed via button {btnName}");
                                return new { success = true, target, invoked = $"button:{btnName}" };
                            }
                            if (btn is NClickableControl clickable)
                            {
                                clickable.EmitSignal("Released", (NButton?)null);
                                ModEntry.WriteLog($"[navigate_menu] proceed via NClickableControl {btnName}");
                                return new { success = true, target, invoked = $"Released:{btnName}" };
                            }
                        }
                    }

                    return new { error = $"Could not proceed on {screenObj.GetType().Name}" };
                }

                default:
                    return new { error = $"Unknown target: {target}. Valid: continue, compendium, card_library, new_run, abandon, back, proceed" };
            }
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static object ClickNode(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("params", out var p) || !p.TryGetProperty("path", out var pathProp))
                return new { error = "click_node requires params.path (Godot node path)" };

            var path = pathProp.GetString() ?? "";

            var tree = GodotEngine.GetMainLoop() as SceneTree;
            if (tree?.Root == null)
                return new { error = "SceneTree not available" };

            var node = tree.Root.GetNodeOrNull(path);
            if (node == null)
                return new { error = $"Node not found: {path}" };

            // Try emitting pressed signal (for BaseButton subclasses)
            if (node is BaseButton button)
            {
                button.EmitSignal("pressed");
                ModEntry.WriteLog($"[click_node] Emitted 'pressed' on BaseButton at {path}");
                return new { success = true, path, node_type = node.GetType().Name, method = "EmitSignal(pressed)" };
            }

            // Try calling Pressed, OnPressed, etc.
            if (TryInvokeMethod(node, ["Pressed", "OnPressed", "_Pressed", "OnClicked", "Click"], Array.Empty<object?>(), out var invokedMethod))
            {
                ModEntry.WriteLog($"[click_node] Invoked {invokedMethod} on {path}");
                return new { success = true, path, node_type = node.GetType().Name, method = invokedMethod };
            }

            // Try GrabFocus + accept event
            if (node is Control control)
            {
                control.GrabFocus();
                var acceptEvent = new InputEventAction { Action = "ui_accept", Pressed = true };
                control.EmitSignal(Control.SignalName.GuiInput, acceptEvent);
                ModEntry.WriteLog($"[click_node] Sent ui_accept to control at {path}");
                return new { success = true, path, node_type = node.GetType().Name, method = "ui_accept" };
            }

            return new { error = $"Don't know how to click {node.GetType().Name} at {path}" };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    // ─── Card Finder / Tilt Tester ──────────────────────────────────────────

    private static object FindCards(JsonElement root)
    {
        try
        {
            float setRotation = 0f;
            bool doRotate = false;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("rotation", out var rotProp))
                {
                    setRotation = (float)rotProp.GetDouble();
                    doRotate = true;
                }
            }

            var tree = GodotEngine.GetMainLoop() as SceneTree;
            if (tree?.Root == null)
                return new { error = "SceneTree not available" };

            var results = new List<object>();
            int totalNodes = 0;
            FindCardsRecursive(tree.Root, results, ref totalNodes, doRotate, setRotation);

            // Also update the known card IDs for the tilt loop
            _knownCardIds.Clear();
            foreach (var r2 in results)
            {
                if (r2 is Dictionary<string, object?> info && info.ContainsKey("_instanceId"))
                    _knownCardIds.Add((ulong)info["_instanceId"]!);
            }

            return new
            {
                total_nodes = totalNodes,
                cards_found = results.Count,
                cards = results,
                rotation_applied = doRotate ? setRotation : (float?)null,
            };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static void FindCardsRecursive(Node node, List<object> results, ref int totalNodes, bool doRotate, float rotation)
    {
        totalNodes++;

        // Check by type name (handles assembly mismatch) AND by is check
        bool isCard = node.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.Cards.NCard"
                    || node is MegaCrit.Sts2.Core.Nodes.Cards.NCard;

        if (isCard && node is Control ctrl)
        {
            var info = new Dictionary<string, object?>
            {
                ["name"] = ctrl.Name.ToString(),
                ["type"] = node.GetType().Name,
                ["position"] = $"{ctrl.GlobalPosition}",
                ["size"] = $"{ctrl.Size}",
                ["rotation"] = ctrl.RotationDegrees,
                ["visible"] = ctrl.Visible,
                ["_instanceId"] = ctrl.GetInstanceId(),
            };

            // Check for portrait
            var portrait = ctrl.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait != null)
            {
                info["portrait_visible"] = portrait.Visible;
                info["portrait_material"] = portrait.Material?.GetType().Name;
                info["portrait_texture"] = portrait.Texture != null;
            }

            // Compute mouse-relative position and apply tilt
            try
            {
                // Use DisplayServer for mouse, and Body child for card rect (NCard itself has size 0)
                var screenMouse = DisplayServer.MouseGetPosition();
                var winPos = DisplayServer.WindowGetPosition();
                var mousePos = new Vector2(screenMouse.X - winPos.X, screenMouse.Y - winPos.Y);

                // Get the Body/CardContainer child's rect (has actual visual size)
                var body = ctrl.GetNodeOrNull<Control>("%CardContainer");
                var cRect = body != null ? body.GetGlobalRect() : ctrl.GetGlobalRect();
                // Fallback: use portrait rect if body also has no size
                if (cRect.Size.X < 1 || cRect.Size.Y < 1)
                {
                    cRect = portrait.GetGlobalRect();
                }
                if (cRect.Size.X > 1 && cRect.Size.Y > 1)
                {
                    var cCenter = cRect.Position + cRect.Size * 0.5f;
                    var rel = (mousePos - cCenter) / (cRect.Size * 0.5f);
                    rel = rel.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

                    bool isOver = cRect.HasPoint(mousePos);
                    float prox = isOver ? 1.0f : Mathf.Max(0, 1.0f - (rel.Length() - 1.0f) * 2.0f);

                    // Update foil shader light_angle (drives both rainbow effect AND perspective tilt)
                    var portrait2 = ctrl.GetNodeOrNull<TextureRect>("%Portrait");
                    if (portrait2?.Material is ShaderMaterial sm)
                    {
                        try
                        {
                            var cur = sm.GetShaderParameter("light_angle").AsVector2();
                            sm.SetShaderParameter("light_angle", cur.Lerp(rel, 0.3f));
                        }
                        catch { sm.SetShaderParameter("light_angle", rel); }
                    }

                    info["mouse_relative"] = $"({rel.X:F2},{rel.Y:F2})";
                }
            }
            catch { }

            if (doRotate)
            {
                // Use rotation value as the Y-axis flip angle (degrees)
                // cos(angle) gives scale_x: 1=front, 0=edge, -1=back
                float angleRad = rotation * Mathf.Pi / 180.0f;
                float scaleX = Mathf.Cos(angleRad);

                // Find the Body (CardContainer) — it has the visual content
                var flipBody = ctrl.GetNodeOrNull<Control>("%CardContainer");
                if (flipBody != null)
                {
                    // Scale X to simulate Y-axis rotation
                    // PivotOffset centers the flip
                    flipBody.PivotOffset = new Vector2(150, 211); // half of card size 300x422
                    flipBody.Scale = new Vector2(scaleX, 1.0f);
                    flipBody.RotationDegrees = 0;
                    info["flip_angle"] = rotation;
                    info["scale_x"] = scaleX;
                }
                else
                {
                    // Fallback: just rotate
                    ctrl.PivotOffset = ctrl.Size * 0.5f;
                    ctrl.RotationDegrees = rotation;
                    info["rotation_set"] = rotation;
                }
            }

            results.Add(info);
        }

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { FindCardsRecursive(node.GetChild(i), results, ref totalNodes, doRotate, rotation); }
            catch { }
        }
    }

    // ─── Auto-Rotate ─────────────────────────────────────────────────────

    private static bool _autoRotate = false;
    private static float _autoRotateAngle = 0f;
    private static float _autoRotateSpeed = 2f; // degrees per tick

    private static object StartAutoRotate()
    {
        _autoRotate = true;
        _autoRotateAngle = 0f;
        ModEntry.WriteLog("[AutoRotate] Started");

        // Start a background task that calls FindCards repeatedly
        System.Threading.Tasks.Task.Run(async () =>
        {
            var emptyJson = System.Text.Json.JsonDocument.Parse("{\"params\":{}}").RootElement;
            while (_autoRotate)
            {
                try
                {
                    // Increment angle
                    _autoRotateAngle = (_autoRotateAngle + _autoRotateSpeed) % 360f;

                    // Apply rotation via MainThreadDispatcher.Post (fire-and-forget)
                    MainThreadDispatcher.Post(() =>
                    {
                        try
                        {
                            var tree = GodotEngine.GetMainLoop() as SceneTree;
                            if (tree?.Root == null) return;
                            AutoRotateCards(tree.Root);
                        }
                        catch { }
                    });
                }
                catch { }
                await System.Threading.Tasks.Task.Delay(33); // ~30fps
            }
            ModEntry.WriteLog("[AutoRotate] Stopped");
        });

        return new { success = true, status = "started" };
    }

    private static object StopAutoRotate()
    {
        _autoRotate = false;

        // Reset all cards to normal
        MainThreadDispatcher.Post(() =>
        {
            try
            {
                var tree = GodotEngine.GetMainLoop() as SceneTree;
                if (tree?.Root == null) return;
                ResetCardScale(tree.Root);
            }
            catch { }
        });

        return new { success = true, status = "stopped" };
    }

    private static void AutoRotateCards(Node node)
    {
        if (node is MegaCrit.Sts2.Core.Nodes.Cards.NCard && node is Control ctrl)
        {
            try
            {
                var body = ctrl.GetNodeOrNull<Control>("%CardContainer");
                if (body != null)
                {
                    float angleRad = _autoRotateAngle * Mathf.Pi / 180.0f;
                    float tiltX = Mathf.Sin(angleRad) * 0.4f; // tilt parameter for vertex shader

                    // Apply vertex shader to CardContainer if not already set
                    if (_tiltShader == null)
                    {
                        _tiltShader = new Shader();
                        _tiltShader.Code = TiltShaderCode;
                    }

                    var mat = body.Material as ShaderMaterial;
                    if (mat == null || mat.Shader != _tiltShader)
                    {
                        mat = new ShaderMaterial();
                        mat.Shader = _tiltShader;
                        body.Material = mat;

                        // Set UseParentMaterial on visual children only
                        SetUseParentOnChildren(body);
                    }

                    mat.SetShaderParameter("tilt_x", tiltX);
                    mat.SetShaderParameter("tilt_y", 0f);
                }
            }
            catch { }
        }

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { AutoRotateCards(node.GetChild(i)); } catch { }
        }
    }

    private static void SetUseParentOnChildren(Node parent)
    {
        for (int i = 0; i < parent.GetChildCount(); i++)
        {
            try
            {
                var child = parent.GetChild(i);
                // Skip ALL text-related nodes
                var tn = child.GetType().Name;
                if (tn.Contains("Label") || tn.Contains("RichText") || tn.Contains("MegaLabel") || tn.Contains("MegaRich"))
                    continue;
                if (child is Label || child is RichTextLabel)
                    continue;

                if (child is CanvasItem ci)
                    ci.UseParentMaterial = true;

                SetUseParentOnChildren(child);
            }
            catch { }
        }
    }

    private static void ResetCardScale(Node node)
    {
        if (node is MegaCrit.Sts2.Core.Nodes.Cards.NCard && node is Control ctrl)
        {
            var body = ctrl.GetNodeOrNull<Control>("%CardContainer");
            if (body != null)
            {
                body.Scale = new Vector2(1.0f, 1.0f);
            }
        }

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { ResetCardScale(node.GetChild(i)); } catch { }
        }
    }

    // ─── Continuous Card Tilt Loop ─────────────────────────────────────────

    private static bool _cardTiltLoopRunning = false;

    private static object StartCardTiltLoop()
    {
        if (_cardTiltLoopRunning)
            return new { success = true, status = "already_running" };

        _cardTiltLoopRunning = true;

        // Create a minimal JsonElement for FindCards with no params
        var emptyJson = System.Text.Json.JsonDocument.Parse("{\"params\":{}}").RootElement;

        System.Threading.Tasks.Task.Run(async () =>
        {
            ModEntry.WriteLog("[CardTiltLoop] Started");
            while (_cardTiltLoopRunning)
            {
                try
                {
                    // FindCards via MainThreadDispatcher.Invoke — THE ONLY PATH THAT WORKS
                    // It discovers cards, applies foil, updates mouse-driven tilt, all on main thread
                    MainThreadDispatcher.Invoke(() => FindCards(emptyJson));
                }
                catch { }
                await System.Threading.Tasks.Task.Delay(50); // ~20fps
            }
            ModEntry.WriteLog("[CardTiltLoop] Stopped");
        });

        return new { success = true, status = "started" };
    }

    private static object StopCardTiltLoop()
    {
        _cardTiltLoopRunning = false;
        return new { success = true, status = "stopped" };
    }

    // ─── Card Tilt Test ────────────────────────────────────────────────────

    private static object CardTiltTest(JsonElement root)
    {
        try
        {
            float tiltX = 0f;
            if (root.TryGetProperty("params", out var p) && p.TryGetProperty("tilt", out var tp))
                tiltX = (float)tp.GetDouble();

            var tree = GodotEngine.GetMainLoop() as SceneTree;
            if (tree?.Root == null) return new { error = "no tree" };

            var results = new List<object>();
            CardTiltRecursive(tree.Root, results, tiltX);
            return new { cards_processed = results.Count, cards = results };
        }
        catch (Exception ex) { return new { error = ex.Message }; }
    }

    private static readonly string TiltShaderCode = @"
shader_type canvas_item;
uniform float tilt_x = 0.0;
uniform float tilt_y = 0.0;
void fragment() {
    vec2 c = UV - 0.5;
    float persp = 1.0 + c.x * tilt_x + c.y * tilt_y * 0.5;
    persp = max(persp, 0.15);
    vec2 uv = vec2(c.x / persp, c.y / persp) + 0.5;
    uv = clamp(uv, vec2(0.0), vec2(1.0));
    float facing = clamp(1.0 + c.x * tilt_x * 0.5, 0.7, 1.3);
    vec4 col = texture(TEXTURE, uv);
    col.rgb *= facing;
    COLOR = col;
}
";
    private static Shader? _tiltShader;

    private static void CardTiltRecursive(Node node, List<object> results, float tiltX)
    {
        if (node is MegaCrit.Sts2.Core.Nodes.Cards.NCard && node is Control card)
        {
            try
            {
                // Find the CardContainer (Body) — this holds ALL visual elements
                var body = card.GetNodeOrNull<Control>("%CardContainer");
                if (body == null)
                {
                    results.Add(new { name = card.Name.ToString(), error = "no CardContainer" });
                    // List children to find the right one
                    var childNames = new List<string>();
                    for (int i = 0; i < card.GetChildCount(); i++)
                    {
                        var ch = card.GetChild(i);
                        childNames.Add($"{ch.Name}({ch.GetType().Name} {ch.GetClass()})");
                    }
                    results.Add(new { children = childNames });
                    return;
                }

                var info = new Dictionary<string, object?>
                {
                    ["name"] = card.Name.ToString(),
                    ["body_size"] = $"{body.Size}",
                    ["body_class"] = body.GetClass(),
                    ["body_type"] = body.GetType().Name,
                    ["body_material"] = body.Material?.GetType().Name,
                };

                // Tilt is handled by foil shader on portrait — no UseParentMaterial needed

                results.Add(info);
            }
            catch (Exception ex)
            {
                results.Add(new { name = card.Name.ToString(), error = ex.Message });
            }
        }

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { CardTiltRecursive(node.GetChild(i), results, tiltX); } catch { }
        }
    }

    // ─── Continuous Foil Tilt Loop ──────────────────────────────────────────

    private static bool _foilTiltRunning = false;
    private static int _tiltDebugCount = 0;
    private static int _tiltMouseLog = 0;
    private static readonly List<ulong> _knownCardIds = new();
    private const float FoilMaxTilt = 15.0f;
    private const float FoilTiltLerp = 0.15f;
    private const float FoilLightLerp = 0.15f;

    private static object StartFoilTilt()
    {
        if (_foilTiltRunning)
            return new { success = true, status = "already_running" };

        _foilTiltRunning = true;

        System.Threading.Tasks.Task.Run(async () =>
        {
            ModEntry.WriteLog("[FoilTilt] Started");
            while (_foilTiltRunning)
            {
                try
                {
                    // Discover cards periodically (blocking call, every ~1s)
                    if (_refreshCounter++ % 30 == 0)
                    {
                        try { MainThreadDispatcher.Invoke(() => RefreshCardList()); }
                        catch { }
                    }

                    // Apply tilt via Post (fire-and-forget, doesn't block/deadlock)
                    if (_knownCardIds.Count > 0)
                        MainThreadDispatcher.Post(() => ApplyTiltToKnownCards());
                }
                catch { }
                await System.Threading.Tasks.Task.Delay(50); // ~20fps
            }
            ModEntry.WriteLog("[FoilTilt] Stopped");
        });

        return new { success = true, status = "started" };
    }

    private static object StopFoilTilt()
    {
        _foilTiltRunning = false;
        return new { success = true, status = "stopped" };
    }

    private static int _refreshCounter = 0;

    private static void RefreshCardList()
    {
        // Only rebuild if empty — find_cards populates this too
        if (_knownCardIds.Count > 0) return;

        var tree = GodotEngine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        CollectCardIds(tree.Root);
    }

    private static void CollectCardIds(Node node)
    {
        if (node is MegaCrit.Sts2.Core.Nodes.Cards.NCard)
            _knownCardIds.Add(node.GetInstanceId());

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { CollectCardIds(node.GetChild(i)); } catch { }
        }
    }

    private static int _applyDebug = 0;

    private static void ApplyTiltToKnownCards()
    {
        // Get mouse position (this runs on main thread via Invoke)
        var screenMouse = DisplayServer.MouseGetPosition();
        var winPos = DisplayServer.WindowGetPosition();
        var mousePos = new Vector2(screenMouse.X - winPos.X, screenMouse.Y - winPos.Y);

        foreach (var cardId in _knownCardIds)
        {
            try
            {
                var cardObj = GodotObject.InstanceFromId(cardId);
                if (cardObj is not Control card) continue;

                // Get card rect from CardContainer (Body)
                var body = card.GetNodeOrNull<Control>("%CardContainer");
                var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
                if (portrait == null || !portrait.Visible) continue;

                var rect = body != null ? body.GetGlobalRect() : portrait.GetGlobalRect();
                if (rect.Size.X < 1 || rect.Size.Y < 1) continue;

                var center = rect.Position + rect.Size * 0.5f;
                var rel = (mousePos - center) / (rect.Size * 0.5f);
                rel = rel.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

                bool isOver = rect.HasPoint(mousePos);
                float prox = isOver ? 1.0f : Mathf.Max(0, 1.0f - (rel.Length() - 1.0f) * 2.0f);

                // Update foil shader light_angle
                if (portrait.Material is ShaderMaterial foilMat)
                {
                    try
                    {
                        var cur = foilMat.GetShaderParameter("light_angle").AsVector2();
                        foilMat.SetShaderParameter("light_angle", cur.Lerp(rel, 0.2f));
                    }
                    catch { foilMat.SetShaderParameter("light_angle", rel); }
                }

                // 3D Y-axis tilt via Scale.X on CardContainer
                // Scale.X = cos(tilt_angle) simulates rotation around vertical axis
                if (body != null)
                {
                    // Mouse X position drives the tilt angle (max ~20 degrees)
                    float tiltAngle = rel.X * 20.0f * prox; // degrees
                    float tiltRad = tiltAngle * Mathf.Pi / 180.0f;
                    float targetScaleX = Mathf.Cos(tiltRad);

                    // Smooth lerp current scale toward target
                    float curScaleX = body.Scale.X;
                    float newScaleX = Mathf.Lerp(curScaleX, targetScaleX, 0.15f);

                    body.PivotOffset = new Vector2(150, 211); // card center
                    body.Scale = new Vector2(newScaleX, 1.0f);
                }
            }
            catch { }
        }
    }
}
