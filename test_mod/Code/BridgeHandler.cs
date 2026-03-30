using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Rooms;
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
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using Godot;
using GodotEngine = Godot.Engine;
using System.Security.Cryptography;

namespace MCPTest;

public static class BridgeHandler
{
    private sealed class HotReloadSession
    {
        public string ModKey { get; }
        public AssemblyLoadContext? LoadContext { get; set; }
        public Assembly? LastLoadedAssembly { get; set; }
        public Harmony? HotReloadHarmony { get; set; }

        public HotReloadSession(string modKey)
        {
            ModKey = modKey;
        }
    }

    private sealed class SerializationCacheSnapshot
    {
        public Type? CacheType { get; init; }
        public Dictionary<string, int>? CategoryMap { get; init; }
        public List<string>? CategoryList { get; init; }
        public Dictionary<string, int>? EntryMap { get; init; }
        public List<string>? EntryList { get; init; }
        public int? CategoryBitSize { get; init; }
        public int? EntryBitSize { get; init; }
    }

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
    private static readonly object _hotReloadLock = new();
    private static readonly Dictionary<string, HotReloadSession> _hotReloadSessions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<object> _reloadHistory = new();
    private static bool _defaultAlcResolvingRegistered;
    private static string _activeHotReloadModKey = "";
    private static string _hotReloadProgress = "";
    private const int MaxReloadHistory = 20;
    private static readonly Regex HotReloadAssemblySuffixRegex = new(@"_hr\d{6,8}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string NormalizeHotReloadModKey(string? assemblyNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyNameOrPath))
            return "";
        var fileOrAssemblyName = Path.GetFileNameWithoutExtension(assemblyNameOrPath);
        return HotReloadAssemblySuffixRegex.Replace(fileOrAssemblyName, "");
    }

    private static HotReloadSession GetOrCreateHotReloadSession(string modKey)
    {
        lock (_hotReloadSessions)
        {
            if (!_hotReloadSessions.TryGetValue(modKey, out var session))
            {
                session = new HotReloadSession(modKey);
                _hotReloadSessions[modKey] = session;
            }
            return session;
        }
    }

    private static IEnumerable<Assembly> GetAssembliesForHotReloadMod(string modKey, Assembly? exclude = null)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a =>
                !string.IsNullOrEmpty(a.GetName().Name)
                && string.Equals(NormalizeHotReloadModKey(a.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase)
                && a != exclude);
    }

    private static void SetHotReloadProgress(string modKey, string step)
    {
        _activeHotReloadModKey = modKey;
        _hotReloadProgress = step;
    }

    private static void ClearHotReloadProgress(string modKey)
    {
        if (string.IsNullOrEmpty(modKey) || string.Equals(_activeHotReloadModKey, modKey, StringComparison.OrdinalIgnoreCase))
        {
            _activeHotReloadModKey = "";
            _hotReloadProgress = "";
        }
    }

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
                "hot_reload" => MainThreadDispatcher.Invoke(() => HotReload(root)),
                "reload_localization" => MainThreadDispatcher.Invoke(() => ReloadLocalization()),
                "reload_history" => GetReloadHistory(),
                "hot_reload_progress" => new { step = _hotReloadProgress, in_progress = !string.IsNullOrEmpty(_hotReloadProgress), mod_key = _activeHotReloadModKey },
                "refresh_live_instances" => MainThreadDispatcher.Invoke(() => RefreshLiveInstances()),
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
                "fmod_test" => MainThreadDispatcher.Invoke(() => FmodTest(root)),
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

            // Enemies with intent decomposition and unique entity IDs
            var enemies = new List<object>();
            var entityCounts = new Dictionary<string, int>();
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

                // Generate unique entity ID
                string baseId = creature.Monster?.GetType().Name ?? "unknown";
                if (!entityCounts.TryGetValue(baseId, out int entityCount)) entityCount = 0;
                entityCounts[baseId] = entityCount + 1;
                string entityId = $"{baseId}_{entityCount}";

                enemies.Add(new
                {
                    index = enemyIdx++,
                    entity_id = entityId,
                    name = baseId,
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

            if (screen.StartsWith("COMBAT") || screen == "HAND_SELECT")
            {
                var cm = CombatManager.Instance;
                if (cm?.IsInProgress == true && cm.IsPlayPhase)
                {
                    var combatState = cm.DebugOnlyGetState();
                    if (combatState != null)
                    {
                        // Check for mid-combat hand card selection (exhaust/discard prompts)
                        try
                        {
                            var playerHand = NPlayerHand.Instance;
                            if (playerHand != null && playerHand.IsInCardSelection)
                            {
                                // Add combat_select_card actions for selectable hand cards
                                actions.Add(new { action = "combat_select_card", description = "Select a card from hand for exhaust/discard" });
                                actions.Add(new { action = "combat_confirm_selection", description = "Confirm hand card selection" });
                            }
                        }
                        catch { }

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
            else if (screen == "HAND_SELECT")
            {
                // Mid-combat card selection (exhaust, discard, etc.)
                actions.Add(new { action = "combat_select_card", description = "Select a card from hand" });
                actions.Add(new { action = "combat_confirm_selection" });
            }
            else if (screen == "RELIC_SELECTION")
            {
                // Relic selection screen
                actions.Add(new { action = "select_relic", description = "Select a relic" });
                actions.Add(new { action = "skip_relic_selection" });
            }
            else if (screen == "CARD_REWARD")
            {
                // Post-combat card reward selection (3 cards to choose from)
                actions.Add(new { action = "select_card_reward", description = "Select a card reward" });
                actions.Add(new { action = "skip_card_reward" });
            }
            else if (screen.StartsWith("MENU_"))
            {
                // Unknown overlay/capstone screen — add dismiss/back actions
                actions.Add(new { action = "dismiss", description = "Dismiss this screen (back/close)" });
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

            // Resolve GameMode enum for the new StartNewSingleplayerRun signature
            var gameModeType = Type.GetType("MegaCrit.Sts2.Core.Runs.GameMode, sts2");
            object gameMode = gameModeType != null
                ? Enum.Parse(gameModeType, "Standard")
                : (object)1; // fallback: GameMode.Standard = 1

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
                        seed, gameMode, ascension, null
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
                "dismiss" or "back" or "close" => DismissCurrentScreen(),
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

    /// <summary>
    /// Search a node's descendant tree for a button with one of the given names, then ForceClick it.
    /// Returns the name of the clicked button, or null if not found.
    /// Uses NClickableControl.ForceClick() which is purely signal-based (no mouse simulation).
    /// </summary>
    private static string? TryForceClickChildButton(Godot.Node root, string[] buttonNames, int maxDepth = 5)
    {
        // First try direct child/descendant lookup by name
        foreach (var name in buttonNames)
        {
            try
            {
                // Try unique name (%) lookup first
                var node = root.GetNodeOrNull($"%{name}");
                if (node == null)
                {
                    // Try recursive find
                    node = FindDescendant(root, name, maxDepth);
                }
                if (node == null) continue;

                // Check if visible
                if (node is Godot.Control ctrl && !ctrl.Visible) continue;

                // Try ForceClick (NClickableControl method)
                var forceClick = node.GetType().GetMethod("ForceClick",
                    BindingFlags.Instance | BindingFlags.Public);
                if (forceClick != null)
                {
                    forceClick.Invoke(node, null);
                    return name;
                }

                // Fallback: try EmitSignal("Released")
                try
                {
                    node.EmitSignal("Released", node);
                    return $"{name}:EmitSignal";
                }
                catch { }

                // Fallback for BaseButton: emit "pressed"
                if (node is Godot.BaseButton)
                {
                    node.EmitSignal("pressed");
                    return $"{name}:pressed";
                }
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"TryForceClickChildButton({name}) error: {ex.GetBaseException().Message}");
            }
        }
        return null;
    }

    private static Godot.Node? FindDescendant(Godot.Node parent, string name, int depth)
    {
        if (depth <= 0) return null;
        foreach (var child in parent.GetChildren())
        {
            if (string.Equals(child.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                return child;
            var found = FindDescendant(child, name, depth - 1);
            if (found != null) return found;
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

    /// <summary>
    /// Find all descendant nodes of type T, sorted by visual position (Y then X).
    /// Handles z-order scrambling from NGridCardHolder.OnFocus() calling MoveToFront().
    /// </summary>
    private static List<T> FindAllSortedByPosition<T>(Godot.Node start) where T : Godot.Control
    {
        var list = new List<T>();
        FindAllRecursive(start, list);
        list.Sort((a, b) =>
        {
            int cmp = a.GlobalPosition.Y.CompareTo(b.GlobalPosition.Y);
            return cmp != 0 ? cmp : a.GlobalPosition.X.CompareTo(b.GlobalPosition.X);
        });
        return list;
    }

    private static void FindAllRecursive<T>(Godot.Node node, List<T> found) where T : Godot.Node
    {
        if (!Godot.GodotObject.IsInstanceValid(node)) return;
        if (node is T item) found.Add(item);
        foreach (var child in node.GetChildren())
            FindAllRecursive(child, found);
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

        // Fallback: find and ForceClick the rest site choice button by index
        if (screenObj is Godot.Node screenNode)
        {
            // Map choice name to button index in ChoicesContainer
            int buttonIndex = normalized switch
            {
                "rest" or "heal" => 0,
                "smith" or "upgrade" => 1,
                "recall" => 2,
                "dig" => -1,     // dig/lift are special
                "lift" => -1,
                _ => -1,
            };

            try
            {
                var choicesContainer = screenNode.GetNodeOrNull("%ChoicesContainer")
                    ?? FindDescendant(screenNode, "ChoicesContainer", 4);
                if (choicesContainer != null)
                {
                    // Try by index first
                    if (buttonIndex >= 0 && buttonIndex < choicesContainer.GetChildCount())
                    {
                        var button = choicesContainer.GetChild(buttonIndex);
                        var fc = button.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public);
                        if (fc != null)
                        {
                            fc.Invoke(button, null);
                            ModEntry.WriteLog($"[RestSite] choice={choice} via ForceClick(ChoicesContainer[{buttonIndex}])");
                            return new { success = true, choice = normalized, invoked = $"ForceClick:ChoicesContainer[{buttonIndex}]" };
                        }
                    }

                    // Try by name match on children
                    for (int i = 0; i < choicesContainer.GetChildCount(); i++)
                    {
                        var child = choicesContainer.GetChild(i);
                        var childName = child.Name.ToString().ToLowerInvariant();
                        if (childName.Contains(normalized) || childName.Contains(normalized.Replace("smith", "upgrade")))
                        {
                            var fc = child.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public);
                            if (fc != null)
                            {
                                fc.Invoke(child, null);
                                ModEntry.WriteLog($"[RestSite] choice={choice} via ForceClick({child.Name})");
                                return new { success = true, choice = normalized, invoked = $"ForceClick:{child.Name}" };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"[RestSite] ForceClick fallback error: {ex.GetBaseException().Message}");
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

        var runState = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
        if (runState?.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.MerchantRoom merchantRoom)
            return new { error = "Not in a merchant room" };

        // Auto-open inventory if needed
        var merchUI = GetMemberValue(null, "Instance") as object;
        try
        {
            var nMerchantRoomType = typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom);
            var instanceProp = nMerchantRoomType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            var merchRoomInstance = instanceProp?.GetValue(null);
            if (merchRoomInstance != null)
            {
                var inventoryObj = GetMemberValue(merchRoomInstance, "Inventory");
                var isOpen = GetMemberValue(inventoryObj, "IsOpen");
                if (isOpen is bool open && !open)
                {
                    TryInvokeMethod(merchRoomInstance, ["OpenInventory"], [], out _);
                }
            }
        }
        catch { }

        // Use the game's inventory API directly (like STS2MCP does)
        var inventory = merchantRoom.Inventory;
        var allEntries = inventory.AllEntries.ToList();

        if (index < 0 || index >= allEntries.Count)
            return new { error = $"Index {index} out of range", action = normalized, item_count = allEntries.Count };

        var entry = allEntries[index];
        if (!entry.IsStocked)
            return new { error = "Item is sold out", action = normalized, index };
        if (!entry.EnoughGold)
            return new { error = $"Not enough gold (need {entry.Cost})", action = normalized, index, cost = entry.Cost };

        // Fire-and-forget purchase using game's own purchase API
        _ = entry.OnTryPurchaseWrapper(inventory);

        ModEntry.WriteLog($"[ShopAction] {normalized} index={index} purchased for {entry.Cost} gold");
        return new { success = true, action = normalized, index, cost = entry.Cost, invoked = "OnTryPurchaseWrapper" };
    }

    private static object ExecuteRewardSelection(JsonElement p)
    {
        var index = p.TryGetProperty("index", out var idx)
            ? idx.GetInt32()
            : p.TryGetProperty("reward_index", out var rIdx) ? rIdx.GetInt32() : 0;

        var screenObj = GetActiveScreenObject();
        if (screenObj == null)
            return new { error = "No active screen object for REWARD", action = "reward_select" };

        // Try to get reward buttons directly from the known field
        var buttons = GetItemsFromMembers(screenObj, "_rewardButtons");
        if (buttons.Count > 0)
        {
            // Filter to only enabled buttons (like STS2MCP does)
            var enabledButtons = buttons.Where(b => {
                var enabled = GetMemberValue(b, "IsEnabled");
                return enabled is not false;
            }).ToList();

            if (index < 0 || index >= enabledButtons.Count)
                return new { error = $"Index {index} out of range", action = "reward_select", item_count = enabledButtons.Count };

            var button = enabledButtons[index];
            var label = GetRewardLabel(button);

            // Primary: ForceClick the NRewardButton directly (NRewardButton extends NButton extends NClickableControl)
            // This is the cleanest approach - same as STS2MCP
            if (TryInvokeMethod(button, ["ForceClick"], [], out var invokedMethod))
            {
                ModEntry.WriteLog($"[reward_select] index={index} '{label}' via {invokedMethod}");
                return new { success = true, action = "reward_select", index, label, invoked = invokedMethod };
            }

            // Fallback: try RewardCollectedFrom(button) on the screen
            if (TryInvokeMethod(screenObj, ["RewardCollectedFrom"], [button], out invokedMethod))
            {
                ModEntry.WriteLog($"[reward_select] index={index} '{label}' via {invokedMethod}");
                return new { success = true, action = "reward_select", index, label, invoked = invokedMethod };
            }

            return new { error = $"Found button at index {index} but could not invoke selection", action = "reward_select", button_type = button.GetType().Name };
        }

        // Fallback to generic indexed interaction
        return ExecuteIndexedScreenInteraction(
            expectedScreen: "REWARD",
            actionName: "reward_select",
            index: index,
            collectionMembers: ["Rewards", "RewardItems", "AvailableRewards", "Entries", "Choices", "Options"],
            collectionMethods: ["GetRewards", "GetCurrentRewards", "GetChoices"],
            screenMethods: ["SelectReward", "ChooseReward", "TakeReward", "ClaimReward", "Select", "Choose", "OnRewardClicked", "RewardCollectedFrom"],
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

        var cards = GetItemsFromMembers(screenObj, "_cards", "Cards", "CardChoices", "Choices", "SelectableCards", "Options", "_cardChoices");
        if (cards.Count == 0)
            cards = GetItemsFromMethods(screenObj, "GetCards", "GetChoices");

        var results = new List<object>();

        // Check for bundle selection screen (NChooseABundleSelectionScreen)
        if (cards.Count == 0 && screenObj is Godot.Node sn)
        {
            var bundleRow = sn.GetNodeOrNull("%BundleRow");
            if (bundleRow != null && bundleRow.GetChildCount() > 0)
            {
                foreach (var index in indices.Distinct())
                {
                    if (index < 0 || index >= bundleRow.GetChildCount())
                    {
                        results.Add(new { index, label = $"bundle_{index}", success = false, invoked = (string?)null });
                        continue;
                    }
                    var bundle = bundleRow.GetChild(index);
                    var hitbox = GetMemberValue(bundle, "Hitbox");
                    bool clicked = false;
                    string? inv = null;
                    if (hitbox != null)
                    {
                        var fc = hitbox.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public);
                        if (fc != null) { fc.Invoke(hitbox, null); clicked = true; inv = "ForceClick(Hitbox)"; }
                    }
                    if (!clicked && TryInvokeMethod(screenObj, ["OnBundleClicked"], [bundle], out inv))
                        clicked = true;

                    results.Add(new { index, label = $"Bundle {index + 1}", success = clicked, invoked = inv });
                }

                object? bundleConfirm = null;
                if (confirmAfterSelection)
                    bundleConfirm = ConfirmCurrentScreen("card_confirm", "confirm", "proceed");

                return new { success = results.All(r => (bool)(r.GetType().GetProperty("success")?.GetValue(r) ?? false)), selected_count = results.Count, results, confirm = bundleConfirm };
            }
        }

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

        // Fallback: find and ForceClick a Proceed/Continue/Done button in the scene tree
        if (screenObj is Godot.Node screenNode)
        {
            var clicked = TryForceClickChildButton(screenNode, ["ProceedButton", "Proceed", "Continue", "Done", "Leave", "Confirm", "Accept"]);
            if (clicked != null)
            {
                ModEntry.WriteLog($"[{requestedAction}] via ForceClick on {clicked}");
                return new { success = true, action = requestedAction, screen, invoked = $"ForceClick:{clicked}" };
            }
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

        // Fallback: find and ForceClick a Confirm/Proceed button in the screen's scene tree
        if (screenObj is Godot.Node screenNode)
        {
            var clicked = TryForceClickChildButton(screenNode, ["Confirm", "Proceed", "Done", "Accept", "OK"]);
            if (clicked != null)
            {
                ModEntry.WriteLog($"[{requestedAction}] via ForceClick on {clicked}");
                return new { success = true, action = requestedAction, screen, invoked = $"ForceClick:{clicked}" };
            }
        }

        return ProceedCurrentScreen(requestedAction, consoleFallbacks);
    }

    /// <summary>
    /// Dismiss any overlay/capstone/popup screen by finding and clicking Back/Close/Dismiss buttons.
    /// Works for DeckViewScreen, InspectCardScreen, and other MENU_ screens.
    /// </summary>
    private static object DismissCurrentScreen()
    {
        var screen = ScreenDetector.GetCurrentScreen();
        var screenObj = GetActiveScreenObject();

        // Try Close/Back/Dismiss methods on the screen object
        if (screenObj != null && TryInvokeMethod(screenObj, ["Close", "Back", "Dismiss", "Hide", "Pop"], Array.Empty<object?>(), out var invokedMethod))
        {
            ModEntry.WriteLog($"[dismiss] via {invokedMethod}");
            return new { success = true, action = "dismiss", screen, invoked = invokedMethod };
        }

        // Try ForceClick on BackButton/CloseButton/DismissButton
        if (screenObj is Godot.Node screenNode)
        {
            var clicked = TryForceClickChildButton(screenNode, ["BackButton", "Back", "CloseButton", "Close", "DismissButton", "Dismiss"]);
            if (clicked != null)
            {
                ModEntry.WriteLog($"[dismiss] via ForceClick on {clicked}");
                return new { success = true, action = "dismiss", screen, invoked = $"ForceClick:{clicked}" };
            }
        }

        // Try navigate_menu back as last resort
        return ProceedCurrentScreen("dismiss", "back");
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

        // Fallback: find and ForceClick a Skip/Back button in the screen's scene tree
        if (screenObj is Godot.Node screenNode)
        {
            var clicked = TryForceClickChildButton(screenNode, ["Skip", "Back", "Cancel", "Close"]);
            if (clicked != null)
            {
                ModEntry.WriteLog($"[{requestedAction}] via ForceClick on {clicked}");
                return new { success = true, action = requestedAction, screen, invoked = $"ForceClick:{clicked}" };
            }
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
                                label = GetEventOptionLabel(items[i]),
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

        // NRewardsScreen stores buttons in _rewardButtons (private List<Control>)
        var rewards = GetItemsFromMembers(screenObj, "_rewardButtons", "Rewards", "RewardItems", "AvailableRewards", "Entries", "Choices", "Options");
        if (rewards.Count == 0)
            rewards = GetItemsFromMethods(screenObj, "GetRewards", "GetCurrentRewards", "GetChoices");

        // Also filter out skipped rewards
        var skipped = GetItemsFromMembers(screenObj, "_skippedRewardButtons");
        var skippedSet = new HashSet<object>(skipped);

        for (int i = 0; i < rewards.Count; i++)
        {
            var reward = rewards[i];
            var isSkipped = skippedSet.Contains(reward);
            actions.Add(new
            {
                action = "reward_select",
                reward_index = i,
                label = GetRewardLabel(reward),
                reward_type = reward.GetType().Name,
                skipped = isSkipped,
            });
        }

        actions.Add(new { action = "reward_proceed" });
        return actions;
    }

    private static string GetEventOptionLabel(object? optionOrButton)
    {
        if (optionOrButton == null) return "unknown";

        // Could be an NEventOptionButton (has .Option property) or an EventOption directly
        var option = GetMemberValue(optionOrButton, "Option") ?? optionOrButton;

        var titleStr = GetLocStringText(GetMemberValue(option, "Title"));
        var descStr = GetLocStringText(GetMemberValue(option, "Description"));

        if (!string.IsNullOrWhiteSpace(titleStr) && !string.IsNullOrWhiteSpace(descStr))
            return $"{StripBBCode(titleStr)}: {StripBBCode(descStr)}";
        if (!string.IsNullOrWhiteSpace(titleStr))
            return StripBBCode(titleStr);
        if (!string.IsNullOrWhiteSpace(descStr))
            return StripBBCode(descStr);

        // Fallback: try reading the label text directly from the Godot node
        if (optionOrButton is Godot.Node node)
        {
            try
            {
                var textNode = node.GetNodeOrNull("%Text") ?? node.GetNodeOrNull("Text");
                if (textNode != null)
                {
                    var text = GetMemberValue(textNode, "Text")?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return StripBBCode(text);
                }
            }
            catch { }
        }

        return GetReadableLabel(optionOrButton);
    }

    /// <summary>
    /// Extract text from a LocString object (tries GetFormattedText(), then ToString()).
    /// </summary>
    private static string? GetLocStringText(object? locString)
    {
        if (locString == null) return null;
        if (locString is string s) return s;

        // Try GetFormattedText() first (returns BBCode-formatted text)
        try
        {
            var method = locString.GetType().GetMethod("GetFormattedText",
                BindingFlags.Instance | BindingFlags.Public);
            if (method != null)
            {
                var result = method.Invoke(locString, null)?.ToString();
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }
        catch { }

        // Fallback to ToString()
        var str = locString.ToString();
        if (!string.IsNullOrWhiteSpace(str) && str != locString.GetType().Name)
            return str;

        return null;
    }

    private static string StripBBCode(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Remove BBCode-style tags like [b], [/b], [color], etc.
        return System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[^\]]+\]", "").Trim();
    }

    private static string GetRewardLabel(object? button)
    {
        if (button == null) return "unknown";

        // NRewardButton has a Reward property with Description (LocString)
        var reward = GetMemberValue(button, "Reward");
        if (reward != null)
        {
            var desc = GetLocStringText(GetMemberValue(reward, "Description"));
            if (!string.IsNullOrWhiteSpace(desc))
                return StripBBCode(desc);
        }

        // Fallback: try reading label text from the Godot node's LabelContainer
        if (button is Godot.Node node)
        {
            try
            {
                // NRewardButton has LabelContainer/Text child
                var labelNode = node.GetNodeOrNull("LabelContainer");
                if (labelNode != null)
                {
                    foreach (var child in labelNode.GetChildren())
                    {
                        var text = GetMemberValue(child, "Text")?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return StripBBCode(text);
                    }
                }
            }
            catch { }
        }

        // Fall back to generic label extraction
        return GetReadableLabel(button);
    }

    private static List<object> GetShopActionDescriptors()
    {
        var actions = new List<object>();
        var screenObj = GetActiveScreenObject();
        if (screenObj != null)
        {
            // If we're on NMerchantRoom (not the inventory), auto-open the inventory
            var typeName = screenObj.GetType().Name;
            if (typeName.Contains("MerchantRoom") && !typeName.Contains("Inventory"))
            {
                try
                {
                    var openMethod = screenObj.GetType().GetMethod("OpenInventory", BindingFlags.Instance | BindingFlags.Public);
                    if (openMethod != null)
                    {
                        openMethod.Invoke(screenObj, null);
                        ModEntry.WriteLog("[Shop] Auto-opened inventory from MerchantRoom");
                        // Re-get the active screen object which should now be NMerchantInventory
                        screenObj = GetActiveScreenObject();
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.WriteLog($"[Shop] Auto-open inventory failed: {ex.GetBaseException().Message}");
                }
            }

            // NMerchantInventory: get all slots via GetAllSlots() which returns NMerchantSlot nodes
            var allSlots = new List<object>();
            try
            {
                var method = screenObj.GetType().GetMethod("GetAllSlots", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    var result = method.Invoke(screenObj, null);
                    if (result is IEnumerable enumerable)
                        foreach (var slot in enumerable) { if (slot != null) allSlots.Add(slot); }
                }
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"GetShopActionDescriptors GetAllSlots error: {ex.GetBaseException().Message}");
            }

            for (int i = 0; i < allSlots.Count; i++)
            {
                var slot = allSlots[i];
                var entry = GetMemberValue(slot, "Entry");
                var cost = entry != null ? GetNumericMember(entry, "Cost") : null;
                var isStocked = entry != null ? GetMemberValue(entry, "IsStocked") : null;
                var slotType = slot.GetType().Name;

                // Determine item type from slot class name
                string itemType = slotType switch
                {
                    var s when s.Contains("Card") && s.Contains("Removal") => "remove",
                    var s when s.Contains("Card") => "card",
                    var s when s.Contains("Relic") => "relic",
                    var s when s.Contains("Potion") => "potion",
                    _ => "unknown"
                };

                // Get label from the entry or slot
                string label = GetShopSlotLabel(slot, entry);

                actions.Add(new
                {
                    action = "shop_buy",
                    item_type = itemType,
                    index = i,
                    label,
                    cost,
                    in_stock = isStocked is bool b ? b : true,
                    slot_type = slotType,
                });
            }

            // Fallback: try legacy approach if no slots found
            if (allSlots.Count == 0)
            {
                // Try Inventory property path
                var inventory = GetMemberValue(screenObj, "Inventory");
                if (inventory != null)
                {
                    AddShopEntryActions(actions, inventory, "card", "CharacterCardEntries", "ColorlessCardEntries", "CardEntries");
                    AddShopEntryActions(actions, inventory, "relic", "RelicEntries");
                    AddShopEntryActions(actions, inventory, "potion", "PotionEntries");
                }
                else
                {
                    AddShopItemActions(actions, screenObj, "card", "Cards", "CardOffers", "AvailableCards");
                    AddShopItemActions(actions, screenObj, "relic", "Relics", "RelicOffers", "AvailableRelics");
                    AddShopItemActions(actions, screenObj, "potion", "Potions", "PotionOffers", "AvailablePotions");
                }
            }
        }

        actions.Add(new { action = "shop_proceed" });
        return actions;
    }

    private static string GetShopSlotLabel(object slot, object? entry)
    {
        if (entry != null)
        {
            // MerchantCardEntry has Card, MerchantRelicEntry has Relic, etc.
            foreach (var memberName in new[] { "Card", "Relic", "Potion", "Name", "DisplayName", "Title" })
            {
                var value = GetMemberValue(entry, memberName);
                if (value != null)
                {
                    var name = GetMemberValue(value, "Name") ?? GetMemberValue(value, "DisplayName");
                    if (name is string s && !string.IsNullOrWhiteSpace(s))
                        return s;
                    var nameStr = name?.ToString();
                    if (!string.IsNullOrWhiteSpace(nameStr) && nameStr != name?.GetType().Name)
                        return nameStr;
                    return value.GetType().Name;
                }
            }
        }
        return GetReadableLabel(slot);
    }

    private static void AddShopEntryActions(List<object> actions, object inventory, string itemType, params string[] memberNames)
    {
        var entries = GetItemsFromMembers(inventory, memberNames);
        for (int i = 0; i < entries.Count; i++)
        {
            var cost = GetNumericMember(entries[i], "Cost");
            var isStocked = GetMemberValue(entries[i], "IsStocked");
            actions.Add(new
            {
                action = "shop_buy",
                item_type = itemType,
                index = i,
                label = GetReadableLabel(entries[i]),
                cost,
                in_stock = isStocked is bool b ? b : true,
            });
        }
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
        var cards = GetItemsFromMembers(screenObj, "_cards", "Cards", "CardChoices", "Choices", "SelectableCards", "Options");
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

        // Fallback: for bundle selection screens (NChooseABundleSelectionScreen)
        // Find NCardBundle children in _bundleRow
        if (cards.Count == 0 && screenObj is Godot.Node screenNode)
        {
            try
            {
                var bundleRow = screenNode.GetNodeOrNull("%BundleRow");
                if (bundleRow != null)
                {
                    for (int i = 0; i < bundleRow.GetChildCount(); i++)
                    {
                        var bundle = bundleRow.GetChild(i);
                        actions.Add(new
                        {
                            action = "card_select",
                            card_index = i,
                            label = $"Bundle {i + 1}",
                            card_type = bundle.GetType().Name,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"GetCardSelectionActionDescriptors bundle fallback error: {ex.GetBaseException().Message}");
            }
        }

        // Fallback: for NChooseACardSelectionScreen and similar, look for _cardChoices
        if (actions.Count == 0)
        {
            var cardChoices = GetItemsFromMembers(screenObj, "_cardChoices", "_cardHolders");
            for (int i = 0; i < cardChoices.Count; i++)
            {
                actions.Add(new
                {
                    action = "card_select",
                    card_index = i,
                    label = GetReadableLabel(cardChoices[i]),
                    card_type = cardChoices[i].GetType().Name,
                });
            }
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

    // ─── Hot Reload (Full Entity + Patch + Loc + PCK) ─────────────────────

    private static object HotReload(JsonElement root)
    {
        if (!Monitor.TryEnter(_hotReloadLock))
            return new { error = "Hot reload already in progress. Wait for the current reload to finish." };

        string modKey = "";
        try
        {
            if (root.TryGetProperty("params", out var p)
                && p.TryGetProperty("dll_path", out var dp))
            {
                modKey = NormalizeHotReloadModKey(dp.GetString());
                if (!string.IsNullOrEmpty(modKey))
                    SetHotReloadProgress(modKey, "starting");
            }
            return HotReloadInner(root, modKey);
        }
        finally
        {
            ClearHotReloadProgress(modKey);
            Monitor.Exit(_hotReloadLock);
        }
    }

    private static object HotReloadInner(JsonElement root, string requestedModKey)
    {
        var actions = new List<string>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var changedEntities = new List<object>();
        int entitiesRemoved = 0, entitiesInjected = 0, poolsUnfrozen = 0, poolRegs = 0, patchCount = 0;
        int entitiesSkipped = 0;
        int verified = 0, verifyFailed = 0;
        int mutableOk = 0, mutableFailed = 0;
        int liveRefreshed = 0;
        bool locReloaded = false;
        bool pckReloaded = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stepTimings = new Dictionary<string, long>();
        long lastLap = 0;

        // Parse params
        string? dllPath = null;
        string? pckPath = null;
        int tier = 2;
        JsonElement poolRegistrations = default;
        bool hasPoolRegs = false;
        string modKey = requestedModKey;
        Assembly? assembly = null;
        string? assemblyName = null;
        int depsLoaded = 0;
        bool alcCollectible = false;
        HotReloadSession? session = null;
        AssemblyLoadContext? priorLoadContext = null;
        Harmony? priorHotReloadHarmony = null;
        AssemblyLoadContext? stagedLoadContext = null;
        Harmony? stagedHotReloadHarmony = null;
        bool sessionCommitted = false;
        SerializationCacheSnapshot? serializationSnapshot = null;
        List<Type> newModelTypes = [];
        var previousModAssemblyRefs = new List<(object mod, FieldInfo assemblyField, Assembly previousAssembly)>();

        void CleanupStagedHotReloadArtifacts()
        {
            if (sessionCommitted)
                return;

            if (stagedHotReloadHarmony != null)
            {
                try
                {
                    stagedHotReloadHarmony.UnpatchAll(stagedHotReloadHarmony.Id);
                    actions.Add("staged_harmony_unpatched");
                }
                catch (Exception cleanupEx)
                {
                    warnings.Add($"staged_harmony_cleanup: {cleanupEx.Message}");
                }
                stagedHotReloadHarmony = null;
            }

            if (stagedLoadContext != null)
            {
                UnloadCollectibleLoadContext(stagedLoadContext, warnings, "staged_alc_cleanup");
                stagedLoadContext = null;
            }
        }

        object Finish()
        {
            var result = new
            {
                success = errors.Count == 0,
                tier,
                assembly_name = assemblyName,
                patch_count = patchCount,
                entities_removed = entitiesRemoved,
                entities_injected = entitiesInjected,
                pools_unfrozen = poolsUnfrozen,
                pool_registrations_applied = poolRegs,
                localization_reloaded = locReloaded,
                pck_reloaded = pckReloaded,
                live_instances_refreshed = liveRefreshed,
                mutable_check_passed = mutableOk,
                mutable_check_failed = mutableFailed,
                alc_collectible = alcCollectible,
                total_ms = sw.ElapsedMilliseconds,
                step_timings = stepTimings,
                timestamp = DateTime.UtcNow.ToString("o"),
                changed_entities = changedEntities,
                actions,
                errors,
                warnings,
            };

            lock (_reloadHistory)
            {
                _reloadHistory.Add(result);
                while (_reloadHistory.Count > MaxReloadHistory)
                    _reloadHistory.RemoveAt(0);
            }

            ModEntry.WriteLog($"[HotReload] {(errors.Count == 0 ? "Complete" : "Failed")} — actions: {actions.Count}, errors: {errors.Count}, verified: {verified}, live_refreshed: {liveRefreshed}");
            EventTracker.Record("hot_reload", $"Tier {tier} {(errors.Count == 0 ? "complete" : "failed")}: {entitiesInjected} entities, {patchCount} patches, {errors.Count} errors, {verified} verified, {liveRefreshed} live");
            return result;
        }

        if (root.TryGetProperty("params", out var p))
        {
            if (p.TryGetProperty("dll_path", out var dp)) dllPath = dp.GetString();
            if (p.TryGetProperty("pck_path", out var pp)) pckPath = pp.GetString();
            if (p.TryGetProperty("tier", out var tp)) tier = tp.GetInt32();
            if (p.TryGetProperty("pool_registrations", out poolRegistrations) && poolRegistrations.ValueKind == JsonValueKind.Array)
                hasPoolRegs = true;
        }

        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
        {
            errors.Add($"dll_not_found: {dllPath}");
            return Finish();
        }

        tier = Math.Clamp(tier, 1, 3);
        modKey = string.IsNullOrEmpty(modKey) ? NormalizeHotReloadModKey(dllPath) : modKey;
        session = GetOrCreateHotReloadSession(modKey);
        priorLoadContext = session.LoadContext;
        priorHotReloadHarmony = session.HotReloadHarmony;
        ModEntry.WriteLog($"[HotReload] Starting tier {tier} reload from {dllPath}");
        EventTracker.Record("hot_reload", $"Tier {tier} from {dllPath} ({modKey})");

        // ── Step 1: Load assembly via collectible ALC + dependency DLLs ──
        SetHotReloadProgress(modKey, "loading_assembly");
        try
        {
            var modDir = Path.GetDirectoryName(dllPath)!;
            var mainDllName = Path.GetFileNameWithoutExtension(dllPath);
            string[] sharedDlls = { "GodotSharp", "0Harmony", "sts2" };

            // Load dependency DLLs into default context (shared types must live here)
            // If a dep DLL file is newer than the loaded version, load it into a
            // fresh collectible ALC so the new main assembly can pick it up.
            foreach (var depDll in Directory.GetFiles(modDir, "*.dll"))
            {
                var depName = Path.GetFileNameWithoutExtension(depDll);
                if (sharedDlls.Any(s => string.Equals(s, depName, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(depName, mainDllName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(NormalizeHotReloadModKey(depName), modKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == depName);
                if (existing == null)
                {
                    try
                    {
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(depDll));
                        depsLoaded++;
                        ModEntry.WriteLog($"[HotReload] Loaded dependency: {depName}");
                    }
                    catch (Exception depEx)
                    {
                        warnings.Add($"dep_load_{depName}: {depEx.Message}");
                    }
                }
                else
                {
                    // Check if the on-disk DLL is newer (different version) than what's loaded
                    try
                    {
                        var onDiskVersion = AssemblyName.GetAssemblyName(Path.GetFullPath(depDll)).Version;
                        var loadedVersion = existing.GetName().Version;
                        if (onDiskVersion != null && loadedVersion != null && onDiskVersion != loadedVersion)
                        {
                            warnings.Add($"dep_stale_{depName}: loaded={loadedVersion}, on_disk={onDiskVersion}. " +
                                "Dependency DLL changes require a game restart to take effect.");
                            ModEntry.WriteLog($"[HotReload] Stale dependency: {depName} loaded={loadedVersion} on_disk={onDiskVersion}");
                        }
                    }
                    catch { /* version check is best-effort */ }
                }
            }

            // Use default ALC for entity-bearing mods (tier 2+) because cross-ALC
            // type identity breaks ModelDb.Inject, entity cloning, and runtime casts.
            // The caller stamps a unique assembly name (e.g., MyMod_hr14523045) so
            // the default ALC accepts it even if a previous version is loaded.
            // Use collectible ALC only for tier 1 (patch-only) where type identity
            // doesn't matter and memory reclaim is beneficial.
            if (tier <= 1)
            {
                try
                {
                    var alc = new AssemblyLoadContext($"HotReload-{DateTime.Now.Ticks}", isCollectible: true);
                    alc.Resolving += (ctx, name) =>
                    {
                        return AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == name.Name);
                    };
                    assembly = alc.LoadFromAssemblyPath(dllPath);
                    stagedLoadContext = alc;
                    alcCollectible = true;
                    ModEntry.WriteLog($"[HotReload] Loaded assembly into collectible ALC: {assembly.FullName}");
                }
                catch (Exception alcEx)
                {
                    warnings.Add($"collectible_alc_fallback: {alcEx.Message}");
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
                    ModEntry.WriteLog($"[HotReload] Fallback to default ALC: {assembly.FullName}");
                }
            }
            else
            {
                // Tier 2+: default ALC for full type compatibility.
                // Register a Resolving handler to redirect version-mismatched
                // dependencies to already-loaded assemblies (e.g., mod references
                // BaseLib 0.2.1.0 but game loaded BaseLib 0.1.0.0).
                if (!_defaultAlcResolvingRegistered)
                {
                    AssemblyLoadContext.Default.Resolving += DefaultAlcResolving;
                    _defaultAlcResolvingRegistered = true;
                }
                assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
                ModEntry.WriteLog($"[HotReload] Loaded into default ALC (tier {tier}): {assembly.FullName}");
            }

            assemblyName = assembly.FullName;
            actions.Add(alcCollectible ? "assembly_loaded_collectible" : "assembly_loaded");
            if (depsLoaded > 0)
                actions.Add($"dependencies_loaded:{depsLoaded}");
            ModEntry.WriteLog($"[HotReload] Assembly: {assembly.FullName} (+{depsLoaded} deps, collectible={alcCollectible})");
        }
        catch (Exception ex)
        {
            errors.Add($"assembly_load: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Assembly load failed: {ex}");
            ExceptionMonitor.Record(ex, "HotReload.AssemblyLoad");
            CleanupStagedHotReloadArtifacts();
            return Finish();
        }
        stepTimings["step1_assembly_load"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 2: Stage new Harmony patches ──
        SetHotReloadProgress(modKey, "patching_harmony");
        try
        {
            var hotReloadId = $"com.mcptest.hotreload.{modKey}.{DateTime.UtcNow.Ticks}";
            stagedHotReloadHarmony = new Harmony(hotReloadId);
            stagedHotReloadHarmony.PatchAll(assembly);
            var patchedMethods = Harmony.GetAllPatchedMethods().ToList();
            patchCount = patchedMethods
                .Select(m => Harmony.GetPatchInfo(m))
                .Where(info => info != null)
                .Select(info => info!.Prefixes.Count(pa => pa.owner == hotReloadId)
                              + info.Postfixes.Count(pa => pa.owner == hotReloadId)
                              + info.Transpilers.Count(pa => pa.owner == hotReloadId))
                .Sum();
            actions.Add("harmony_staged");
            ModEntry.WriteLog($"[HotReload] Harmony staged under {hotReloadId}, {patchCount} patches");
        }
        catch (Exception ex)
        {
            errors.Add($"harmony: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Harmony error: {ex}");
            CleanupStagedHotReloadArtifacts();
        }
        stepTimings["step2_harmony_patch"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // Tier 1 stops here
        if (tier < 2)
        {
            if (stagedHotReloadHarmony != null)
            {
                session!.HotReloadHarmony = stagedHotReloadHarmony;
                session.LoadContext = stagedLoadContext;
                session.LastLoadedAssembly = assembly;
                sessionCommitted = true;
                if (priorHotReloadHarmony != null && priorHotReloadHarmony.Id != stagedHotReloadHarmony.Id)
                {
                    try
                    {
                        priorHotReloadHarmony.UnpatchAll(priorHotReloadHarmony.Id);
                        actions.Add("previous_harmony_unpatched");
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"previous_harmony_unpatch: {ex.Message}");
                    }
                }

                var staleRemoved = RemoveStalePatchesForMod(modKey, assembly);
                if (staleRemoved > 0)
                    actions.Add($"stale_patches_removed:{staleRemoved}");

                if (priorLoadContext != null && !ReferenceEquals(priorLoadContext, stagedLoadContext))
                    UnloadCollectibleLoadContext(priorLoadContext, warnings, "prior_alc_unload");

                actions.Add("harmony_repatched");
            }
            else
            {
                errors.Add("harmony: no staged hot-reload Harmony instance was created");
            }

            return Finish();
        }

        // ── Step 3: Update Mod.assembly reference in ModManager ──
        SetHotReloadProgress(modKey, "updating_mod_reference");
        try
        {
            var loadedModsField = typeof(ModManager).GetField("_loadedMods", BindingFlags.NonPublic | BindingFlags.Static);
            if (loadedModsField != null)
            {
                var loadedMods = loadedModsField.GetValue(null) as IList;
                int updatedModRefs = 0;
                if (loadedMods != null)
                {
                    foreach (var mod in loadedMods)
                    {
                        var asmProp = mod.GetType().GetField("assembly", BindingFlags.Public | BindingFlags.Instance);
                        var currentAsm = asmProp?.GetValue(mod) as Assembly;
                        if (asmProp != null
                            && currentAsm != null
                            && string.Equals(NormalizeHotReloadModKey(currentAsm.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase))
                        {
                            previousModAssemblyRefs.Add((mod, asmProp, currentAsm));
                            asmProp.SetValue(mod, assembly);
                            updatedModRefs++;
                        }
                    }
                }
                if (updatedModRefs > 0)
                {
                    actions.Add("mod_reference_updated");
                    ModEntry.WriteLog($"[HotReload] Updated Mod.assembly on {updatedModRefs} loaded mod reference(s) for {modKey}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"mod_ref: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Mod reference update error: {ex}");
        }
        stepTimings["step3_mod_reference"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 4: Invalidate ReflectionHelper._modTypes cache ──
        SetHotReloadProgress(modKey, "invalidating_reflection_cache");
        try
        {
            var modTypesField = typeof(ReflectionHelper).GetField("_modTypes", BindingFlags.NonPublic | BindingFlags.Static);
            modTypesField?.SetValue(null, null);
            actions.Add("reflection_cache_invalidated");
            ModEntry.WriteLog("[HotReload] ReflectionHelper._modTypes invalidated");
        }
        catch (Exception ex)
        {
            errors.Add($"reflection_cache: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Reflection cache error: {ex}");
        }
        stepTimings["step4_reflection_cache"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 5: Register new entity IDs in ModelIdSerializationCache ──
        SetHotReloadProgress(modKey, "registering_entity_ids");
        // The cache maps category/entry names to net IDs for serialization.
        // Built at boot time, so hot-reloaded entities aren't in it. We must
        // register them BEFORE constructing any ModelId objects.
        // Collect entity types and sort so dependencies are injected first:
        // Powers before Cards (cards may reference powers via PowerVar<T>),
        // Monsters before Encounters (encounters reference monsters).
        newModelTypes = GetLoadableTypes(assembly, warnings, "new_assembly_types")
            .Where(t => !t.IsAbstract && !t.IsInterface && InheritsFromByName(t, "AbstractModel"))
            .OrderBy(t => GetInjectionPriority(t))
            .ToList();

        try
        {
            var cacheType = typeof(ModelId).Assembly.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache");
            if (cacheType != null)
            {
                serializationSnapshot = CaptureSerializationCacheSnapshot(cacheType);
                var categoryMap = cacheType.GetField("_categoryNameToNetIdMap", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) as Dictionary<string, int>;
                var categoryList = cacheType.GetField("_netIdToCategoryNameMap", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) as List<string>;
                var entryMap = cacheType.GetField("_entryNameToNetIdMap", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) as Dictionary<string, int>;
                var entryList = cacheType.GetField("_netIdToEntryNameMap", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) as List<string>;

                int registered = 0;
                foreach (var newType in newModelTypes)
                {
                    var (category, entry) = GetCategoryAndEntry(newType);
                    if (categoryMap != null && categoryList != null && !categoryMap.ContainsKey(category))
                    {
                        categoryMap[category] = categoryList.Count;
                        categoryList.Add(category);
                    }
                    if (entryMap != null && entryList != null && !entryMap.ContainsKey(entry))
                    {
                        entryMap[entry] = entryList.Count;
                        entryList.Add(entry);
                        registered++;
                    }
                }
                if (registered > 0)
                {
                    // Update bit sizes (used by network serialization)
                    var catBitProp = cacheType.GetProperty("CategoryIdBitSize", BindingFlags.Public | BindingFlags.Static);
                    var entBitProp = cacheType.GetProperty("EntryIdBitSize", BindingFlags.Public | BindingFlags.Static);
                    if (catBitProp?.SetMethod != null && categoryList != null)
                        catBitProp.SetValue(null, ComputeBitSize(categoryList.Count));
                    if (entBitProp?.SetMethod != null && entryList != null)
                        entBitProp.SetValue(null, ComputeBitSize(entryList.Count));

                    actions.Add($"serialization_cache_updated:{registered}");
                    ModEntry.WriteLog($"[HotReload] Registered {registered} new entity IDs in ModelIdSerializationCache");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"serialization_cache: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Serialization cache update warning: {ex}");
        }
        stepTimings["step5_serialization_cache"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 6: Transactionally replace entities in ModelDb ──
        SetHotReloadProgress(modKey, "reloading_entities");
        var entitySnapshot = new Dictionary<ModelId, AbstractModel>();
        try
        {
            var contentByIdField = typeof(ModelDb).GetField("_contentById", BindingFlags.NonPublic | BindingFlags.Static);
            var typedDict = contentByIdField?.GetValue(null) as Dictionary<ModelId, AbstractModel>;
            if (typedDict == null)
                throw new InvalidOperationException("ModelDb._contentById not found");

            var affectedIds = new HashSet<ModelId>();
            var oldTypeSignatures = new Dictionary<string, int>(StringComparer.Ordinal);
            var removedTypeNames = new Dictionary<ModelId, string>();
            foreach (var oldAssembly in GetAssembliesForHotReloadMod(modKey, assembly))
            {
                foreach (var oldType in GetLoadableTypes(oldAssembly, warnings, $"old_assembly_types:{oldAssembly.FullName}")
                    .Where(t => !t.IsAbstract && !t.IsInterface && InheritsFromByName(t, "AbstractModel")))
                {
                    try
                    {
                        var id = BuildModelId(oldType);
                        affectedIds.Add(id);
                        removedTypeNames[id] = oldType.Name;
                        if (typedDict.TryGetValue(id, out var existing))
                            entitySnapshot[id] = existing;
                        oldTypeSignatures[oldType.FullName ?? oldType.Name] = ComputeTypeSignatureHash(oldType);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"snapshot_{oldType.Name}: {ex.Message}");
                    }
                }
            }

            foreach (var newType in newModelTypes)
            {
                try
                {
                    var id = BuildModelId(newType);
                    affectedIds.Add(id);
                    if (typedDict.TryGetValue(id, out var existing))
                        entitySnapshot[id] = existing;
                }
                catch (Exception ex)
                {
                    warnings.Add($"snapshot_{newType.Name}: {ex.Message}");
                }
            }

            var stagedModels = new Dictionary<ModelId, AbstractModel>();
            foreach (var newType in newModelTypes)
            {
                try
                {
                    var id = BuildModelId(newType);
                    var fullName = newType.FullName ?? newType.Name;
                    if (oldTypeSignatures.TryGetValue(fullName, out var oldHash)
                        && entitySnapshot.TryGetValue(id, out var existing)
                        && ComputeTypeSignatureHash(newType) == oldHash)
                    {
                        stagedModels[id] = existing;
                        entitiesSkipped++;
                        changedEntities.Add(new { name = newType.Name, action = "unchanged" });
                        continue;
                    }

                    var instance = Activator.CreateInstance(newType);
                    if (instance is not AbstractModel model)
                        throw new InvalidOperationException($"{newType.FullName} is not assignable to AbstractModel at runtime");

                    model.InitId(id);
                    stagedModels[id] = model;
                    entitiesInjected++;
                    changedEntities.Add(new { name = newType.Name, action = "injected", id = id.ToString() });
                    ModEntry.WriteLog($"[HotReload] Staged: {newType.Name} as {id}");
                }
                catch (Exception ex)
                {
                    errors.Add($"inject_{newType.Name}: {ex.Message}");
                }
            }

            if (errors.Any(e => e.StartsWith("inject_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Entity staging failed; ModelDb changes were not committed.");

            int removedThisCommit = 0;
            foreach (var id in affectedIds)
            {
                if (typedDict.Remove(id))
                    removedThisCommit++;
            }
            entitiesRemoved = removedThisCommit;

            foreach (var (id, removedName) in removedTypeNames)
            {
                if (!stagedModels.ContainsKey(id))
                    changedEntities.Add(new { name = removedName, action = "removed" });
            }

            foreach (var (id, model) in stagedModels)
                typedDict[id] = model;

            actions.Add("entities_reregistered");
            if (entitiesSkipped > 0)
                actions.Add($"entities_unchanged:{entitiesSkipped}");
            ModEntry.WriteLog($"[HotReload] Entities: {entitiesRemoved} removed, {entitiesInjected} injected, {entitiesSkipped} unchanged (skipped)");
        }
        catch (Exception ex)
        {
            errors.Add($"entity_reload: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Entity reload error: {ex}");

            try
            {
                var contentByIdField = typeof(ModelDb).GetField("_contentById", BindingFlags.NonPublic | BindingFlags.Static);
                var rollbackDict = contentByIdField?.GetValue(null) as Dictionary<ModelId, AbstractModel>;
                if (rollbackDict != null)
                    RestoreEntitySnapshot(rollbackDict, entitySnapshot, modKey);
            }
            catch (Exception rollbackEx)
            {
                errors.Add($"rollback_entities: {rollbackEx.Message}");
            }

            foreach (var (modRef, asmField, previousAssembly) in previousModAssemblyRefs)
            {
                try
                {
                    asmField.SetValue(modRef, previousAssembly);
                }
                catch (Exception rollbackEx)
                {
                    warnings.Add($"rollback_mod_ref: {rollbackEx.Message}");
                }
            }

            try
            {
                var modTypesField = typeof(ReflectionHelper).GetField("_modTypes", BindingFlags.NonPublic | BindingFlags.Static);
                modTypesField?.SetValue(null, null);
            }
            catch (Exception rollbackEx)
            {
                warnings.Add($"rollback_reflection_cache: {rollbackEx.Message}");
            }

            RestoreSerializationCacheSnapshot(serializationSnapshot);
            CleanupStagedHotReloadArtifacts();
            return Finish();
        }
        stepTimings["step6_entity_reload"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 7: Null ModelDb cached enumerables ──
        SetHotReloadProgress(modKey, "clearing_modeldb_caches");
        try
        {
            string[] cacheFields = {
                "_allCards", "_allCardPools", "_allCharacterCardPools",
                "_allSharedEvents", "_allEvents", "_allEncounters", "_allPotions",
                "_allPotionPools", "_allCharacterPotionPools", "_allSharedPotionPools",
                "_allPowers", "_allRelics", "_allCharacterRelicPools", "_achievements"
            };
            foreach (var fieldName in cacheFields)
            {
                var field = typeof(ModelDb).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
                field?.SetValue(null, null);
            }
            actions.Add("modeldb_caches_cleared");
            ModEntry.WriteLog("[HotReload] ModelDb caches cleared");
        }
        catch (Exception ex)
        {
            errors.Add($"modeldb_caches: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] ModelDb cache clear error: {ex}");
        }
        stepTimings["step7_modeldb_caches"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 8: Unfreeze pools + re-register pool content ──
        SetHotReloadProgress(modKey, "refreshing_pools");
        try
        {
            var poolResult = UnfreezeAndReregisterPools(assembly, modKey, hasPoolRegs ? poolRegistrations : default, hasPoolRegs, errors, warnings);
            poolsUnfrozen = poolResult.unfrozen;
            poolRegs = poolResult.registered;
            actions.Add("pools_refreshed");
            ModEntry.WriteLog($"[HotReload] Pools unfrozen: {poolsUnfrozen}, registered: {poolRegs}");
        }
        catch (Exception ex)
        {
            errors.Add($"pool_refresh: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Pool refresh error: {ex}");
        }
        stepTimings["step8_pools"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 9: Reload localization ──
        SetHotReloadProgress(modKey, "reloading_localization");
        try
        {
            var locManager = LocManager.Instance;
            if (locManager != null)
            {
                locManager.SetLanguage(locManager.Language);
                locReloaded = true;
                actions.Add("localization_reloaded");
                ModEntry.WriteLog("[HotReload] Localization reloaded");
            }
            else
            {
                warnings.Add("LocManager.Instance is null");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"localization: {ex.Message}");
            ModEntry.WriteLog($"[HotReload] Localization reload error: {ex}");
        }
        stepTimings["step9_localization"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 10 (Tier 3): Remount PCK ──
        SetHotReloadProgress(modKey, "remounting_pck");
        if (tier >= 3 && !string.IsNullOrEmpty(pckPath) && File.Exists(pckPath))
        {
            try
            {
                bool loaded = ProjectSettings.LoadResourcePack(pckPath);
                if (loaded)
                {
                    pckReloaded = true;
                    actions.Add("pck_remounted");
                    ModEntry.WriteLog($"[HotReload] PCK remounted: {pckPath}");

                    // Re-trigger loc reload to pick up new PCK loc files
                    try
                    {
                        LocManager.Instance?.SetLanguage(LocManager.Instance.Language);
                    }
                    catch { /* already reported above if loc system is broken */ }
                }
                else
                {
                    errors.Add($"pck_load_failed: Godot returned false for {pckPath}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"pck: {ex.Message}");
                ModEntry.WriteLog($"[HotReload] PCK reload error: {ex}");
            }
        }
        else if (tier >= 3 && string.IsNullOrEmpty(pckPath))
        {
            warnings.Add("Tier 3 requested but no pck_path provided");
        }

        if (!alcCollectible)
            warnings.Add("Old assembly loaded into default ALC (non-collectible); memory will accumulate");
        stepTimings["step10_pck"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 11: Verify injected entities exist in ModelDb ──
        SetHotReloadProgress(modKey, "verifying_entities");
        // Also try ToMutable() on cards to catch PowerVar<T> resolution failures early.
        if (tier >= 2 && entitiesInjected > 0)
        {
            try
            {
                var injectedTypes = GetLoadableTypes(assembly, null, null)
                    .Where(t => !t.IsAbstract && !t.IsInterface
                        && (t.IsSubclassOf(typeof(AbstractModel)) || InheritsFromByName(t, "AbstractModel")))
                    .OrderBy(t => GetInjectionPriority(t))
                    .ToList();
                foreach (var type in injectedTypes)
                {
                    if (ModelDb.Contains(type))
                        verified++;
                    else
                        verifyFailed++;
                }
                if (verifyFailed > 0)
                    warnings.Add($"verify: {verifyFailed}/{injectedTypes.Count} injected types missing from ModelDb");
                else
                    actions.Add($"verified:{verified}_entities_in_modeldb");

                // ToMutable sanity check on injected cards
                var contentByIdField = typeof(ModelDb).GetField("_contentById", BindingFlags.NonPublic | BindingFlags.Static);
                var typedDict = contentByIdField?.GetValue(null) as Dictionary<ModelId, AbstractModel>;
                if (typedDict != null)
                {
                    foreach (var cardType in injectedTypes.Where(t => InheritsFromByName(t, "CardModel")))
                    {
                        try
                        {
                            var cardId = BuildModelId(cardType);
                            if (typedDict.TryGetValue(cardId, out var cardModel) && cardModel is CardModel card)
                            {
                                var mutable = card.ToMutable();
                                mutableOk++;
                                ModEntry.WriteLog($"[HotReload] ToMutable OK: {cardType.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            mutableFailed++;
                            var inner = ex.InnerException ?? ex;
                            warnings.Add($"ToMutable_{cardType.Name}: {inner.GetType().Name}: {inner.Message}");
                            ModEntry.WriteLog($"[HotReload] ToMutable FAILED: {cardType.Name}: {inner}");
                        }
                    }
                    if (mutableOk > 0) actions.Add($"mutable_check_passed:{mutableOk}");
                    if (mutableFailed > 0) actions.Add($"mutable_check_failed:{mutableFailed}");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"verify_error: {ex.Message}");
            }
        }
        stepTimings["step11_verify"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        // ── Step 12: Refresh live instances (cards/relics/powers in scene tree) ──
        SetHotReloadProgress(modKey, "refreshing_live_instances");
        if (tier >= 2 && entitiesInjected > 0)
        {
            try
            {
                var refreshResult = RefreshLiveInstances();
                var totalProp = refreshResult.GetType().GetProperty("total_refreshed");
                if (totalProp != null)
                    liveRefreshed = (int)totalProp.GetValue(refreshResult)!;
                if (liveRefreshed > 0)
                    actions.Add($"live_instances_refreshed:{liveRefreshed}");
                ModEntry.WriteLog($"[HotReload] Live instances refreshed: {liveRefreshed}");
            }
            catch (Exception ex)
            {
                warnings.Add($"live_refresh: {ex.Message}");
            }

            // Run-scoped refresh: update mutable card/relic instances in the current run
            try
            {
                int runRefreshed = RefreshRunInstances(assembly, modKey);
                if (runRefreshed > 0)
                {
                    actions.Add($"run_instances_refreshed:{runRefreshed}");
                    ModEntry.WriteLog($"[HotReload] Run instances refreshed: {runRefreshed}");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"run_refresh: {ex.Message}");
            }
        }
        stepTimings["step12_live_refresh"] = sw.ElapsedMilliseconds - lastLap;
        lastLap = sw.ElapsedMilliseconds;

        try
        {
            if (stagedHotReloadHarmony != null)
            {
                session!.HotReloadHarmony = stagedHotReloadHarmony;
                actions.Add("harmony_repatched");
            }
            session!.LoadContext = stagedLoadContext;
            session.LastLoadedAssembly = assembly;
            sessionCommitted = true;

            if (priorHotReloadHarmony != null
                && stagedHotReloadHarmony != null
                && priorHotReloadHarmony.Id != stagedHotReloadHarmony.Id)
            {
                try
                {
                    priorHotReloadHarmony.UnpatchAll(priorHotReloadHarmony.Id);
                    actions.Add("previous_harmony_unpatched");
                }
                catch (Exception ex)
                {
                    warnings.Add($"previous_harmony_unpatch: {ex.Message}");
                }
            }

            var staleRemoved = RemoveStalePatchesForMod(modKey, assembly);
            if (staleRemoved > 0)
            {
                actions.Add($"stale_patches_removed:{staleRemoved}");
                ModEntry.WriteLog($"[HotReload] Removed {staleRemoved} stale patches from old assemblies for {modKey}");
            }

            if (priorLoadContext != null && !ReferenceEquals(priorLoadContext, stagedLoadContext))
                UnloadCollectibleLoadContext(priorLoadContext, warnings, "prior_alc_unload");
        }
        catch (Exception ex)
        {
            errors.Add($"session_commit: {ex.Message}");
            warnings.Add($"session_commit_warning: {ex.Message}");
        }

        return Finish();
    }

    private static int ComputeBitSize(int count)
    {
        return count <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(count));
    }

    private static SerializationCacheSnapshot? CaptureSerializationCacheSnapshot(Type cacheType)
    {
        return new SerializationCacheSnapshot
        {
            CacheType = cacheType,
            CategoryMap = (cacheType.GetField("_categoryNameToNetIdMap", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as Dictionary<string, int>) is { } categoryMap
                ? new Dictionary<string, int>(categoryMap)
                : null,
            CategoryList = (cacheType.GetField("_netIdToCategoryNameMap", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as List<string>) is { } categoryList
                ? [.. categoryList]
                : null,
            EntryMap = (cacheType.GetField("_entryNameToNetIdMap", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as Dictionary<string, int>) is { } entryMap
                ? new Dictionary<string, int>(entryMap)
                : null,
            EntryList = (cacheType.GetField("_netIdToEntryNameMap", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as List<string>) is { } entryList
                ? [.. entryList]
                : null,
            CategoryBitSize = cacheType.GetProperty("CategoryIdBitSize", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as int?,
            EntryBitSize = cacheType.GetProperty("EntryIdBitSize", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as int?,
        };
    }

    private static void RestoreSerializationCacheSnapshot(SerializationCacheSnapshot? snapshot)
    {
        if (snapshot?.CacheType == null)
            return;

        var cacheType = snapshot.CacheType;
        cacheType.GetField("_categoryNameToNetIdMap", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, snapshot.CategoryMap != null ? new Dictionary<string, int>(snapshot.CategoryMap) : null);
        cacheType.GetField("_netIdToCategoryNameMap", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, snapshot.CategoryList != null ? new List<string>(snapshot.CategoryList) : null);
        cacheType.GetField("_entryNameToNetIdMap", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, snapshot.EntryMap != null ? new Dictionary<string, int>(snapshot.EntryMap) : null);
        cacheType.GetField("_netIdToEntryNameMap", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, snapshot.EntryList != null ? new List<string>(snapshot.EntryList) : null);

        var catBitProp = cacheType.GetProperty("CategoryIdBitSize", BindingFlags.Public | BindingFlags.Static);
        if (catBitProp?.SetMethod != null && snapshot.CategoryBitSize.HasValue)
            catBitProp.SetValue(null, snapshot.CategoryBitSize.Value);
        var entryBitProp = cacheType.GetProperty("EntryIdBitSize", BindingFlags.Public | BindingFlags.Static);
        if (entryBitProp?.SetMethod != null && snapshot.EntryBitSize.HasValue)
            entryBitProp.SetValue(null, snapshot.EntryBitSize.Value);
    }

    private static void RestoreEntitySnapshot(Dictionary<ModelId, AbstractModel> target, Dictionary<ModelId, AbstractModel> snapshot, string modKey)
    {
        var snapshotIds = snapshot.Keys.ToHashSet();
        foreach (var existingId in target.Keys.ToList())
        {
            if (snapshotIds.Contains(existingId))
                continue;
            if (target[existingId] is AbstractModel model
                && string.Equals(NormalizeHotReloadModKey(model.GetType().Assembly.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase))
            {
                target.Remove(existingId);
            }
        }

        foreach (var (id, model) in snapshot)
            target[id] = model;
    }

    private static void UnloadCollectibleLoadContext(AssemblyLoadContext loadContext, List<string> warnings, string warningPrefix)
    {
        try
        {
            loadContext.Unload();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch (Exception ex)
        {
            warnings.Add($"{warningPrefix}: {ex.Message}");
        }
    }

    private static int RemoveStalePatchesForMod(string modKey, Assembly currentAssembly)
    {
        int staleRemoved = 0;
        foreach (var method in Harmony.GetAllPatchedMethods().ToList())
        {
            var patchInfo = Harmony.GetPatchInfo(method);
            if (patchInfo == null)
                continue;

            foreach (var patch in patchInfo.Prefixes.Concat(patchInfo.Postfixes).Concat(patchInfo.Transpilers))
            {
                if (patch.PatchMethod?.DeclaringType?.Assembly is not Assembly patchAsm)
                    continue;
                if (patchAsm == currentAssembly)
                    continue;
                if (!string.Equals(NormalizeHotReloadModKey(patchAsm.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var ownerHarmony = new Harmony(patch.owner);
                    ownerHarmony.Unpatch(method, patch.PatchMethod);
                    staleRemoved++;
                }
                catch
                {
                    // best effort
                }
            }
        }

        return staleRemoved;
    }

    private static object GetReloadHistory()
    {
        lock (_reloadHistory)
        {
            return new
            {
                count = _reloadHistory.Count,
                max_stored = MaxReloadHistory,
                history = _reloadHistory.ToArray(),
            };
        }
    }

    private static (int unfrozen, int registered) UnfreezeAndReregisterPools(
        Assembly newAssembly,
        string modKey,
        JsonElement poolRegistrations,
        bool hasPoolRegs,
        List<string> errors,
        List<string> warnings)
    {
        int unfrozen = 0;
        int registered = 0;

        // Collect the type names from the reloaded assembly so we only remove
        // entries belonging to this mod, not other mods' pool registrations.
        var reloadedTypeNames = new HashSet<string>(
            GetLoadableTypes(newAssembly, warnings, "pool_type_scan")
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Select(t => t.FullName ?? t.Name));
        // Also include old assembly type names so stale entries get cleaned up
        foreach (var oldAsm in GetAssembliesForHotReloadMod(modKey, newAssembly))
        {
            foreach (var t in GetLoadableTypes(oldAsm, null, null).Where(t => !t.IsAbstract && !t.IsInterface))
                reloadedTypeNames.Add(t.FullName ?? t.Name);
        }

        // Access ModHelper._moddedContentForPools via reflection
        var poolsField = typeof(ModHelper).GetField("_moddedContentForPools", BindingFlags.NonPublic | BindingFlags.Static);
        if (poolsField == null)
        {
            warnings.Add("ModHelper._moddedContentForPools not found");
            return (0, 0);
        }

        var pools = poolsField.GetValue(null) as IDictionary;
        if (pools != null)
        {
            // Unfreeze pools and remove only entries from the reloaded mod
            foreach (var key in pools.Keys.Cast<object>().ToList())
            {
                var content = pools[key];
                if (content == null) continue;
                var contentType = content.GetType();
                var frozenField = contentType.GetField("isFrozen");
                var modelsField = contentType.GetField("modelsToAdd");
                if (frozenField != null)
                {
                    bool wasFrozen = (bool)frozenField.GetValue(content)!;
                    if (wasFrozen)
                    {
                        frozenField.SetValue(content, false);
                        unfrozen++;
                    }
                }
                // Only remove entries belonging to the reloaded mod, not other mods
                if (modelsField?.GetValue(content) is IList modelsList)
                {
                    for (int i = modelsList.Count - 1; i >= 0; i--)
                    {
                        var entry = modelsList[i];
                        if (entry != null)
                        {
                            var entryTypeName = entry.GetType().FullName ?? entry.GetType().Name;
                            if (reloadedTypeNames.Contains(entryTypeName))
                                modelsList.RemoveAt(i);
                        }
                    }
                }
            }
        }

        // Null pool instance caches so they re-enumerate on next access
        NullPoolInstanceCaches();

        // Re-register pool entries from explicit registrations
        if (hasPoolRegs && poolRegistrations.ValueKind == JsonValueKind.Array)
        {
            foreach (var reg in poolRegistrations.EnumerateArray())
            {
                try
                {
                    string? poolTypeName = reg.TryGetProperty("pool_type", out var pt) ? pt.GetString() : null;
                    string? modelTypeName = reg.TryGetProperty("model_type", out var mt) ? mt.GetString() : null;
                    if (poolTypeName == null || modelTypeName == null) continue;

                    var poolType = FindTypeByName(null, poolTypeName);
                    var modelType = FindTypeByName(newAssembly, modelTypeName, modKey);

                    if (poolType == null)
                    {
                        warnings.Add($"pool_type_not_found: {poolTypeName}");
                        continue;
                    }
                    if (modelType == null)
                    {
                        warnings.Add($"model_type_not_found: {modelTypeName}");
                        continue;
                    }

                    ModHelper.AddModelToPool(poolType, modelType);
                    registered++;
                    ModEntry.WriteLog($"[HotReload] Pool reg (explicit): {modelTypeName} → {poolTypeName}");
                }
                catch (Exception ex)
                {
                    errors.Add($"pool_reg: {ex.Message}");
                }
            }
        }
        else
        {
            // Assembly-level pool discovery: find [Pool(typeof(...))] attributes via reflection
            // This is 100% accurate — reads compiled attributes, not regex on source
            registered = DiscoverAndRegisterPoolsFromAssembly(newAssembly, errors, warnings);
        }

        return (unfrozen, registered);
    }

    private static void NullPoolInstanceCaches()
    {
        // Null lazy caches on pool model base types so they re-enumerate via ConcatModelsFromMods
        // CardPoolModel: _allCards (CardModel[]?), _allCardIds (HashSet<ModelId>?)
        NullInstanceFieldsOnPoolType(typeof(CardPoolModel), "_allCards", "_allCardIds");
        // RelicPoolModel: _relics (IEnumerable<RelicModel>?), _allRelicIds (HashSet<ModelId>?)
        NullInstanceFieldsOnPoolType(typeof(RelicPoolModel), "_relics", "_allRelicIds");
        // PotionPoolModel: _allPotions (IEnumerable<PotionModel>?), _allPotionIds (HashSet<ModelId>?)
        NullInstanceFieldsOnPoolType(typeof(PotionPoolModel), "_allPotions", "_allPotionIds");
    }

    private static void NullInstanceFieldsOnPoolType(Type basePoolType, params string[] fieldNames)
    {
        // Pool instances are singletons stored in ModelDb._contentById
        // We need to null the lazy fields on each concrete pool instance
        var contentByIdField = typeof(ModelDb).GetField("_contentById", BindingFlags.NonPublic | BindingFlags.Static);
        if (contentByIdField == null) return;

        var contentById = contentByIdField.GetValue(null) as IDictionary;
        if (contentById == null) return;

        foreach (var value in contentById.Values)
        {
            if (value == null || !basePoolType.IsAssignableFrom(value.GetType())) continue;
            foreach (var fieldName in fieldNames)
            {
                var field = value.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                // Also check base type's private fields
                if (field == null)
                    field = basePoolType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(value, null);
            }
        }
    }

    /// <summary>
    /// Compute a hash of a type's member signatures for incremental reload comparison.
    /// If the hash matches between old and new assembly, the type is unchanged.
    /// </summary>
    private static int ComputeTypeSignatureHash(Type type)
    {
        unchecked
        {
            var signatures = new List<string>
            {
                $"type:{type.FullName}",
                $"base:{type.BaseType?.FullName ?? ""}",
            };

            signatures.AddRange(type.GetInterfaces()
                .Select(i => $"iface:{i.FullName}")
                .OrderBy(s => s, StringComparer.Ordinal));

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(m => m.ToString(), StringComparer.Ordinal))
            {
                var methodSig = $"{method.Name}|{method.ReturnType.FullName}|{string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName))}";
                try
                {
                    var il = method.GetMethodBody()?.GetILAsByteArray();
                    if (il != null && il.Length > 0)
                        methodSig += $"|il:{Convert.ToHexString(il)}";
                }
                catch { /* some methods do not expose IL */ }
                signatures.Add($"method:{methodSig}");
            }

            signatures.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => $"field:{f.Name}|{f.FieldType.FullName}"));

            signatures.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"prop:{p.Name}|{p.PropertyType.FullName}"));

            foreach (var attr in type.GetCustomAttributesData()
                .OrderBy(a => a.AttributeType.FullName, StringComparer.Ordinal))
            {
                var ctorArgs = string.Join(",", attr.ConstructorArguments.Select(a => a.Value?.ToString() ?? "null"));
                signatures.Add($"attr:{attr.AttributeType.FullName}|{ctorArgs}");
            }

            int hash = unchecked((int)2166136261);
            foreach (var signature in signatures)
            {
                foreach (var ch in signature)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
            }
            return hash;
        }
    }

    /// <summary>
    /// Resolves assembly version mismatches for hot-reloaded mods in the default ALC.
    /// When a mod references e.g. BaseLib 0.2.1.0 but the game loaded BaseLib 0.1.0.0,
    /// this handler redirects by assembly name (ignoring version) to avoid FileNotFoundException.
    /// </summary>
    private static Assembly? DefaultAlcResolving(AssemblyLoadContext context, AssemblyName name)
    {
        var requestedToken = name.GetPublicKeyToken() ?? Array.Empty<byte>();
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(loaded =>
            {
                var candidate = loaded.GetName();
                if (!string.Equals(candidate.Name, name.Name, StringComparison.Ordinal))
                    return false;

                var candidateToken = candidate.GetPublicKeyToken() ?? Array.Empty<byte>();
                bool tokenMatches = requestedToken.Length == 0 || candidateToken.SequenceEqual(requestedToken);
                bool cultureMatches = string.Equals(candidate.CultureName ?? "", name.CultureName ?? "", StringComparison.OrdinalIgnoreCase);
                return tokenMatches && cultureMatches;
            });
    }

    /// <summary>
    /// Returns an injection priority for entity types. Lower = injected first.
    /// Powers before cards (cards reference powers via PowerVar&lt;T&gt;),
    /// monsters before encounters (encounters reference monsters).
    /// </summary>
    private static int GetInjectionPriority(Type type)
    {
        // Walk the inheritance chain and check by name (cross-ALC safe)
        if (InheritsFromByName(type, "PowerModel")) return 0;
        if (InheritsFromByName(type, "RelicModel")) return 1;
        if (InheritsFromByName(type, "PotionModel")) return 2;
        if (InheritsFromByName(type, "MonsterModel")) return 3;
        if (InheritsFromByName(type, "EncounterModel")) return 4;
        if (InheritsFromByName(type, "CardModel")) return 5;  // Last — may reference powers
        if (InheritsFromByName(type, "EventModel")) return 6;
        return 9;
    }

    /// <summary>
    /// Get category and entry strings for a type WITHOUT constructing a ModelId.
    /// Used to register in the serialization cache before ModelId construction.
    /// </summary>
    private static (string category, string entry) GetCategoryAndEntry(Type type)
    {
        var cursor = type;
        while (cursor.BaseType != null && cursor.BaseType.Name != "AbstractModel")
            cursor = cursor.BaseType;
        string category = ModelId.SlugifyCategory(cursor.Name);
        string entry = MegaCrit.Sts2.Core.Helpers.StringHelper.Slugify(type.Name);
        return (category, entry);
    }

    /// <summary>
    /// Build a ModelId for a type, compatible with cross-ALC types.
    /// Replicates ModelDb.GetId logic but uses name-based base type detection.
    /// Must be called AFTER registering the entity in ModelIdSerializationCache.
    /// </summary>
    private static ModelId BuildModelId(Type type)
    {
        var (category, entry) = GetCategoryAndEntry(type);
        return new ModelId(category, entry);
    }

    /// <summary>
    /// Check if a type inherits from a base type by walking the type name chain.
    /// Works across AssemblyLoadContexts where IsSubclassOf fails.
    /// </summary>
    private static bool InheritsFromByName(Type type, string baseTypeName)
    {
        var cursor = type.BaseType;
        while (cursor != null)
        {
            if (cursor.Name == baseTypeName)
                return true;
            cursor = cursor.BaseType;
        }
        return false;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, List<string>? warnings, string? warningPrefix)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            if (warnings != null)
            {
                warnings.Add($"{warningPrefix ?? "types"}: {ex.Message}");
                foreach (var loaderEx in ex.LoaderExceptions.Where(e => e != null).Take(3))
                {
                    warnings.Add($"{warningPrefix ?? "types"}_loader: {loaderEx!.Message}");
                }
            }
            return ex.Types.Where(t => t != null).Cast<Type>();
        }
    }

    private static Type? FindTypeByName(Assembly? preferredAssembly, string typeName, string? preferredModKey = null)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        if (Type.GetType(typeName, throwOnError: false) is Type assemblyQualifiedType)
            return assemblyQualifiedType;

        static bool MatchesRequestedName(Type type, string requestedName, bool requireExactFullName)
        {
            if (string.Equals(type.FullName, requestedName, StringComparison.Ordinal))
                return true;
            return !requireExactFullName && string.Equals(type.Name, requestedName, StringComparison.Ordinal);
        }

        bool requireExactFullName = typeName.Contains('.');
        if (preferredAssembly != null)
        {
            var exactPreferred = preferredAssembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (exactPreferred != null)
                return exactPreferred;

            var preferredMatches = GetLoadableTypes(preferredAssembly, null, null)
                .Where(t => MatchesRequestedName(t, typeName, requireExactFullName))
                .ToList();

            if (preferredMatches.Count == 1)
                return preferredMatches[0];

            var preferredExactFullName = preferredMatches
                .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal));
            if (preferredExactFullName != null)
                return preferredExactFullName;
        }

        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (!string.IsNullOrWhiteSpace(preferredModKey))
        {
            assemblies = assemblies.Where(a =>
                string.Equals(NormalizeHotReloadModKey(a.GetName().Name), preferredModKey, StringComparison.OrdinalIgnoreCase));
        }

        var candidates = assemblies
            .SelectMany(a => GetLoadableTypes(a, null, null))
            .Where(t => MatchesRequestedName(t, typeName, requireExactFullName))
            .GroupBy(t => t.AssemblyQualifiedName ?? $"{t.Assembly.FullName}:{t.FullName ?? t.Name}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (candidates.Count == 0)
            return null;

        var fullNameMatches = candidates
            .Where(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal))
            .ToList();
        if (fullNameMatches.Count == 1)
            return fullNameMatches[0];
        if (fullNameMatches.Count > 1)
        {
            if (!string.IsNullOrWhiteSpace(preferredModKey))
            {
                var scopedMatch = fullNameMatches
                    .FirstOrDefault(t => string.Equals(
                        NormalizeHotReloadModKey(t.Assembly.GetName().Name),
                        preferredModKey,
                        StringComparison.OrdinalIgnoreCase));
                if (scopedMatch != null)
                    return scopedMatch;
            }
            return null;
        }

        if (requireExactFullName)
            return null;

        var shortNameMatches = candidates
            .Where(t => string.Equals(t.Name, typeName, StringComparison.Ordinal))
            .ToList();
        if (shortNameMatches.Count == 1)
            return shortNameMatches[0];
        if (shortNameMatches.Count > 1 && !string.IsNullOrWhiteSpace(preferredModKey))
        {
            var scopedShortMatches = shortNameMatches
                .Where(t => string.Equals(
                    NormalizeHotReloadModKey(t.Assembly.GetName().Name),
                    preferredModKey,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (scopedShortMatches.Count == 1)
                return scopedShortMatches[0];
        }

        return null;
    }

    // ─── Assembly-Level Pool Discovery ────────────────────────────────────

    private static int DiscoverAndRegisterPoolsFromAssembly(Assembly assembly, List<string> errors, List<string> warnings)
    {
        int discovered = 0;
        foreach (var type in GetLoadableTypes(assembly, warnings, "pool_discovery"))
        {
            if (type.IsAbstract || type.IsInterface) continue;
            try
            {
                // Look for [Pool(typeof(...))] attributes by name (BaseLib attribute)
                foreach (var attr in type.GetCustomAttributes(false))
                {
                    var attrType = attr.GetType();
                    if (attrType.Name != "PoolAttribute") continue;

                    // Extract the pool type from the attribute's constructor argument
                    // PoolAttribute typically has a Type property or constructor param
                    var poolTypeProp = attrType.GetProperty("PoolType")
                        ?? attrType.GetProperty("Type")
                        ?? attrType.GetProperty("Pool");
                    Type? poolType = null;
                    if (poolTypeProp != null)
                    {
                        poolType = poolTypeProp.GetValue(attr) as Type;
                    }
                    else
                    {
                        // Try constructor argument via CustomAttributeData
                        var attrData = type.GetCustomAttributesData()
                            .FirstOrDefault(d => d.AttributeType.Name == "PoolAttribute");
                        if (attrData?.ConstructorArguments.Count > 0)
                        {
                            poolType = attrData.ConstructorArguments[0].Value as Type;
                        }
                    }

                    if (poolType == null)
                    {
                        warnings.Add($"pool_attr_no_type: {type.Name} has [Pool] but could not extract pool type");
                        continue;
                    }

                    try
                    {
                        ModHelper.AddModelToPool(poolType, type);
                        discovered++;
                        ModEntry.WriteLog($"[HotReload] Pool reg (assembly): {type.Name} → {poolType.Name}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"pool_auto_reg_{type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"pool_scan_{type.Name}: {ex.Message}");
            }
        }
        if (discovered > 0)
            ModEntry.WriteLog($"[HotReload] Assembly-level pool discovery: {discovered} registrations");
        return discovered;
    }

    // ─── Live Instance Refresh ─────────────────────────────────────────────

    private static object RefreshLiveInstances()
    {
        try
        {
            var root = ((SceneTree)Engine.GetMainLoop()).Root;
            int cardsRefreshed = 0, relicsRefreshed = 0, powersRefreshed = 0, potionsRefreshed = 0, monstersRefreshed = 0;
            var errors = new List<string>();

            // Refresh NCard models
            try
            {
                RefreshNodeModels<MegaCrit.Sts2.Core.Nodes.Cards.NCard>(
                    root, "NCard",
                    node => {
                        var modelProp = node.GetType().GetProperty("Model");
                        var model = modelProp?.GetValue(node) as AbstractModel;
                        if (model == null) return false;
                        var id = model.Id;
                        var fresh = ModelDb.GetByIdOrNull<CardModel>(id);
                        if (fresh == null || ReferenceEquals(fresh, model)) return false;
                        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
                        modelProp!.SetValue(node, fresh);
                        return true;
                    },
                    ref cardsRefreshed);
            }
            catch (Exception ex)
            {
                errors.Add($"cards: {ex.Message}");
            }

            // Refresh NRelic models
            try
            {
                RefreshNodeModels<MegaCrit.Sts2.Core.Nodes.Relics.NRelic>(
                    root, "NRelic",
                    node => {
                        var modelProp = node.GetType().GetProperty("Model");
                        var model = modelProp?.GetValue(node) as AbstractModel;
                        if (model == null) return false;
                        var id = model.Id;
                        var fresh = ModelDb.GetByIdOrNull<RelicModel>(id);
                        if (fresh == null || ReferenceEquals(fresh, model)) return false;
                        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
                        modelProp!.SetValue(node, fresh);
                        return true;
                    },
                    ref relicsRefreshed);
            }
            catch (Exception ex)
            {
                errors.Add($"relics: {ex.Message}");
            }

            // Refresh NPower models
            try
            {
                RefreshNodeModels<MegaCrit.Sts2.Core.Nodes.Combat.NPower>(
                    root, "NPower",
                    node => {
                        var modelProp = node.GetType().GetProperty("Model");
                        var model = modelProp?.GetValue(node) as AbstractModel;
                        if (model == null) return false;
                        var id = model.Id;
                        var fresh = ModelDb.GetByIdOrNull<PowerModel>(id);
                        if (fresh == null || ReferenceEquals(fresh, model)) return false;
                        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
                        modelProp!.SetValue(node, fresh);
                        return true;
                    },
                    ref powersRefreshed);
            }
            catch (Exception ex)
            {
                errors.Add($"powers: {ex.Message}");
            }

            // Refresh NPotion models
            try
            {
                RefreshNodeModels<NPotion>(
                    root, "NPotion",
                    node => {
                        var modelProp = node.GetType().GetProperty("Model");
                        var model = modelProp?.GetValue(node) as AbstractModel;
                        if (model == null) return false;
                        var id = model.Id;
                        var fresh = ModelDb.GetByIdOrNull<PotionModel>(id);
                        if (fresh == null || ReferenceEquals(fresh, model)) return false;
                        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
                        modelProp!.SetValue(node, fresh);
                        return true;
                    },
                    ref potionsRefreshed);
            }
            catch (Exception ex)
            {
                errors.Add($"potions: {ex.Message}");
            }

            // Refresh NCreature models (monsters)
            // v0.101.0: NCreature has Entity (Creature) property, monster model at Entity.Monster
            // Monster is a get-only auto-property — must set via backing field
            try
            {
                RefreshNodeModels<MegaCrit.Sts2.Core.Nodes.Combat.NCreature>(
                    root, "NCreature",
                    node => {
                        var creature = node.Entity;
                        if (creature == null || !creature.IsMonster) return false;
                        var model = creature.Monster;
                        if (model == null) return false;
                        var id = model.Id;
                        var fresh = ModelDb.GetByIdOrNull<MonsterModel>(id);
                        if (fresh == null || ReferenceEquals(fresh, model)) return false;
                        if (fresh.GetType().Assembly == model.GetType().Assembly) return false;
                        // Monster is a get-only auto-property, set via compiler-generated backing field
                        var backingField = creature.GetType().GetField("<Monster>k__BackingField",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (backingField != null)
                        {
                            backingField.SetValue(creature, fresh);
                            fresh.Creature = creature;
                            return true;
                        }
                        return false;
                    },
                    ref monstersRefreshed);
            }
            catch (Exception ex)
            {
                errors.Add($"monsters: {ex.Message}");
            }

            var total = cardsRefreshed + relicsRefreshed + powersRefreshed + potionsRefreshed + monstersRefreshed;
            ModEntry.WriteLog($"[HotReload] Live refresh: {cardsRefreshed} cards, {relicsRefreshed} relics, {powersRefreshed} powers, {potionsRefreshed} potions, {monstersRefreshed} monsters");
            EventTracker.Record("refresh_live_instances", $"{total} refreshed ({cardsRefreshed}C {relicsRefreshed}R {powersRefreshed}P {potionsRefreshed}Pot {monstersRefreshed}M)");

            return new
            {
                success = errors.Count == 0,
                cards_refreshed = cardsRefreshed,
                relics_refreshed = relicsRefreshed,
                powers_refreshed = powersRefreshed,
                potions_refreshed = potionsRefreshed,
                monsters_refreshed = monstersRefreshed,
                total_refreshed = total,
                errors,
            };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"[HotReload] Live refresh error: {ex}");
            ExceptionMonitor.Record(ex, "RefreshLiveInstances");
            return new { error = ex.Message };
        }
    }

    private static object? GetObjectPropertyValue(object? owner, params string[] propertyNames)
    {
        if (owner == null)
            return null;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        foreach (var propertyName in propertyNames)
        {
            var property = owner.GetType().GetProperty(propertyName, flags);
            if (property != null)
                return property.GetValue(owner);
        }

        return null;
    }

    private static IList? GetCollectionItems(object? container, params string[] itemPropertyNames)
    {
        if (container is IList list)
            return list;
        if (container == null)
            return null;

        foreach (var propertyName in itemPropertyNames)
        {
            if (GetObjectPropertyValue(container, propertyName) is IList propertyList)
                return propertyList;
        }

        return null;
    }

    private static AbstractModel? GetCanonicalModel(AbstractModel currentModel)
    {
        return currentModel switch
        {
            CardModel => ModelDb.GetByIdOrNull<CardModel>(currentModel.Id),
            RelicModel => ModelDb.GetByIdOrNull<RelicModel>(currentModel.Id),
            PowerModel => ModelDb.GetByIdOrNull<PowerModel>(currentModel.Id),
            PotionModel => ModelDb.GetByIdOrNull<PotionModel>(currentModel.Id),
            _ => null,
        };
    }

    private static bool TryReadMemberValue(object source, string memberName, out object? value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var property = source.GetType().GetProperty(memberName, flags);
        if (property != null && property.CanRead)
        {
            value = property.GetValue(source);
            return true;
        }

        var field = source.GetType().GetField(memberName, flags);
        if (field != null)
        {
            value = field.GetValue(source);
            return true;
        }

        value = null;
        return false;
    }

    private static bool CanAssignMemberValue(Type targetType, object? value)
    {
        if (value == null)
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
            return true;

        var underlyingNullable = Nullable.GetUnderlyingType(targetType);
        return underlyingNullable != null && underlyingNullable.IsAssignableFrom(valueType);
    }

    private static bool TryWriteMemberValue(object target, string memberName, object? value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var property = target.GetType().GetProperty(memberName, flags);
        if (property != null && property.CanWrite && CanAssignMemberValue(property.PropertyType, value))
        {
            property.SetValue(target, value);
            return true;
        }

        var field = target.GetType().GetField(memberName, flags);
        if (field != null && !field.IsInitOnly && CanAssignMemberValue(field.FieldType, value))
        {
            field.SetValue(target, value);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> GetRuntimeStateMemberNames(AbstractModel model)
    {
        if (model is CardModel)
        {
            return
            [
                "CostForTurn",
                "CurrentCost",
                "TemporaryCost",
                "IsTemporaryCostModified",
                "FreeToPlay",
                "Retain",
                "Ethereal",
                "Exhaust",
                "Exhausts",
                "WasDiscarded",
                "WasDrawnThisTurn",
                "PlayedThisTurn",
                "Misc",
                "Counter",
                "TurnsInHand",
            ];
        }

        if (model is RelicModel)
        {
            return
            [
                "Counter",
                "Charges",
                "UsesRemaining",
                "Cooldown",
                "TriggeredThisTurn",
                "TriggeredThisCombat",
                "PulseActive",
                "IsDisabled",
                "Grayscale",
            ];
        }

        if (model is PowerModel)
        {
            return
            [
                "Stacks",
                "Amount",
                "Counter",
                "TurnsRemaining",
                "TriggeredThisTurn",
                "TriggeredThisCombat",
                "PulseActive",
                "JustApplied",
            ];
        }

        if (model is PotionModel)
        {
            return
            [
                "Charges",
                "UsesRemaining",
                "Counter",
                "TriggeredThisCombat",
            ];
        }

        return Array.Empty<string>();
    }

    private static void CopyNamedRuntimeState(object source, object target, IEnumerable<string> memberNames)
    {
        foreach (var memberName in memberNames.Distinct(StringComparer.Ordinal))
        {
            if (TryReadMemberValue(source, memberName, out var value))
                TryWriteMemberValue(target, memberName, value);
        }
    }

    private static void ApplyCardUpgradeState(object source, object target)
    {
        int upgrades = 0;
        if (TryReadMemberValue(source, "TimesUpgraded", out var timesUpgradedValue) && timesUpgradedValue is int timesUpgraded)
        {
            upgrades = timesUpgraded;
        }
        else if (TryReadMemberValue(source, "UpgradeCount", out var upgradeCountValue) && upgradeCountValue is int upgradeCount)
        {
            upgrades = upgradeCount;
        }
        else if (TryReadMemberValue(source, "IsUpgraded", out var upgradedValue) && upgradedValue is bool isUpgraded && isUpgraded)
        {
            upgrades = 1;
        }

        if (upgrades <= 0)
            return;

        var upgradeMethod = target.GetType().GetMethod("Upgrade", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (upgradeMethod == null)
            return;

        for (int i = 0; i < upgrades; i++)
        {
            try
            {
                upgradeMethod.Invoke(target, null);
            }
            catch
            {
                break;
            }
        }
    }

    private static bool TryRefreshModelInstance(object? instance, string assemblyKey, HashSet<string> reloadedTypeFullNames, out object? replacement)
    {
        replacement = null;
        if (instance is not AbstractModel currentModel)
            return false;

        if (!string.Equals(
                NormalizeHotReloadModKey(currentModel.GetType().Assembly.GetName().Name),
                assemblyKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentTypeName = currentModel.GetType().FullName ?? currentModel.GetType().Name;
        var canonicalModel = GetCanonicalModel(currentModel);
        if (!reloadedTypeFullNames.Contains(currentTypeName))
        {
            if (canonicalModel == null
                || !string.Equals(
                    NormalizeHotReloadModKey(canonicalModel.GetType().Assembly.GetName().Name),
                    assemblyKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // ToMutable() is defined on each model subclass, not on AbstractModel — invoke via reflection
        var toMutableMethod = canonicalModel?.GetType().GetMethod("ToMutable");
        var mutable = toMutableMethod?.Invoke(canonicalModel, null) as AbstractModel;
        if (mutable == null)
            return false;

        if (currentModel is CardModel)
            ApplyCardUpgradeState(currentModel, mutable);

        CopyNamedRuntimeState(currentModel, mutable, GetRuntimeStateMemberNames(currentModel));
        replacement = mutable;
        return true;
    }

    private static int RefreshModelList(IList? items, string assemblyKey, HashSet<string> reloadedTypeFullNames)
    {
        if (items == null)
            return 0;

        int refreshed = 0;
        for (int i = 0; i < items.Count; i++)
        {
            try
            {
                if (TryRefreshModelInstance(items[i], assemblyKey, reloadedTypeFullNames, out var replacement))
                {
                    items[i] = replacement;
                    refreshed++;
                }
            }
            catch
            {
                // Best effort: leave the existing runtime instance in place if migration fails.
            }
        }

        return refreshed;
    }

    /// <summary>
    /// Refresh mutable card/relic/power/potion instances in the current run's state.
    /// Replaces instances belonging to the reloaded mod with fresh ToMutable() copies,
    /// keyed by mod identity and canonical ModelId lookup rather than short type names.
    /// </summary>
    private static int RefreshRunInstances(Assembly reloadedAssembly, string modKey)
    {
        int refreshed = 0;
        string assemblyKey = string.IsNullOrWhiteSpace(modKey)
            ? NormalizeHotReloadModKey(reloadedAssembly.GetName().Name)
            : modKey;
        var reloadedTypeFullNames = new HashSet<string>(
            GetLoadableTypes(reloadedAssembly, null, null)
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Select(t => t.FullName ?? t.Name),
            StringComparer.Ordinal);

        try
        {
            var runManagerType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => GetLoadableTypes(a, null, null))
                .FirstOrDefault(t => t.Name == "RunManager");
            if (runManagerType == null)
                return 0;

            var currentRun = runManagerType.GetProperty("CurrentRun", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (currentRun == null)
                return 0;

            if (GetObjectPropertyValue(currentRun, "Players") is not IEnumerable players)
                return 0;

            foreach (var player in players)
            {
                refreshed += RefreshModelList(
                    GetCollectionItems(GetObjectPropertyValue(player, "MasterDeck", "Deck"), "Cards"),
                    assemblyKey,
                    reloadedTypeFullNames);
                refreshed += RefreshModelList(
                    GetCollectionItems(GetObjectPropertyValue(player, "Relics"), "Items"),
                    assemblyKey,
                    reloadedTypeFullNames);
                refreshed += RefreshModelList(
                    GetCollectionItems(GetObjectPropertyValue(player, "Potions"), "Items"),
                    assemblyKey,
                    reloadedTypeFullNames);

                var playerCombatState = GetObjectPropertyValue(player, "PlayerCombatState", "CombatState");
                if (playerCombatState == null)
                    continue;

                foreach (var pileName in new[] { "Hand", "DrawPile", "DiscardPile", "ExhaustPile" })
                {
                    refreshed += RefreshModelList(
                        GetCollectionItems(GetObjectPropertyValue(playerCombatState, pileName), "Cards"),
                        assemblyKey,
                        reloadedTypeFullNames);
                }

                refreshed += RefreshModelList(
                    GetCollectionItems(GetObjectPropertyValue(playerCombatState, "PlayerPowers", "Powers"), "Items"),
                    assemblyKey,
                    reloadedTypeFullNames);
            }
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"[HotReload] Run instance refresh error: {ex}");
        }

        return refreshed;
    }

    private static void RefreshNodeModels<T>(Node root, string typeName, Func<T, bool> refreshFunc, ref int count) where T : Node
    {
        var queue = new Queue<Node>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is T typed)
            {
                try
                {
                    if (refreshFunc(typed))
                        count++;
                }
                catch { /* skip individual node failures */ }
            }
            for (int i = 0; i < node.GetChildCount(); i++)
                queue.Enqueue(node.GetChild(i));
        }
    }

    private static object ReloadLocalization()
    {
        try
        {
            var locManager = LocManager.Instance;
            if (locManager == null)
                return new { error = "LocManager.Instance is null" };

            var lang = locManager.Language;
            locManager.SetLanguage(lang);
            ModEntry.WriteLog($"[HotReload] Localization reloaded for language: {lang}");
            EventTracker.Record("reload_localization", $"Language: {lang}");
            return new { success = true, language = lang };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"[HotReload] Localization reload error: {ex}");
            ExceptionMonitor.Record(ex, "ReloadLocalization");
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
                return new { error = "navigate_menu requires params.target (continue, compendium, card_library, settings, profile, timeline, multiplayer, new_run, abandon, back)" };

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

                    // Check if there's actually a save to continue
                    // Use SaveManager.HasRunSave (not RunManager.IsInProgress — that's only true after loading)
                    bool hasSave = false;
                    try
                    {
                        var saveManager = typeof(MegaCrit.Sts2.Core.Saves.SaveManager)
                            .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                        if (saveManager != null)
                        {
                            var hasRunSaveProp = saveManager.GetType().GetProperty("HasRunSave", BindingFlags.Instance | BindingFlags.Public);
                            if (hasRunSaveProp != null)
                                hasSave = (bool)(hasRunSaveProp.GetValue(saveManager) ?? false);
                        }
                    }
                    catch { }

                    // Fallback: also check if the Continue button is visible
                    if (!hasSave)
                    {
                        try
                        {
                            var continueBtn = GetMemberValue(mainMenu, "_continueButton");
                            if (continueBtn is Godot.Control ctrl && ctrl.Visible)
                                hasSave = true;
                        }
                        catch { }
                    }

                    if (!hasSave)
                        return new { error = "No saved run to continue" };

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

                case "settings":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    var settingsType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen, sts2")
                        ?? Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSettingsScreen, sts2");
                    if (settingsType == null)
                        return new { error = "Could not find NSettingsScreen type" };

                    var pushMethod = stack.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "PushSubmenuType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    if (pushMethod == null)
                        return new { error = "Could not find PushSubmenuType method" };

                    pushMethod.MakeGenericMethod(settingsType).Invoke(stack, null);
                    ModEntry.WriteLog("[navigate_menu] PushSubmenuType<NSettingsScreen>");
                    return new { success = true, target, invoked = "PushSubmenuType<NSettingsScreen>" };
                }

                case "profile":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    var profileType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen.NProfileScreen, sts2")
                        ?? Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Profile.NProfileScreen, sts2");
                    if (profileType == null)
                        return new { error = "Could not find NProfileScreen type" };

                    var pushMethod = stack.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "PushSubmenuType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    if (pushMethod == null)
                        return new { error = "Could not find PushSubmenuType method" };

                    pushMethod.MakeGenericMethod(profileType).Invoke(stack, null);
                    ModEntry.WriteLog("[navigate_menu] PushSubmenuType<NProfileScreen>");
                    return new { success = true, target, invoked = "PushSubmenuType<NProfileScreen>" };
                }

                case "timeline":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    var timelineType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Timeline.NTimelineScreen, sts2")
                        ?? Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NTimelineScreen, sts2");
                    if (timelineType == null)
                        return new { error = "Could not find NTimelineScreen type" };

                    var pushMethod = stack.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "PushSubmenuType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    if (pushMethod == null)
                        return new { error = "Could not find PushSubmenuType method" };

                    pushMethod.MakeGenericMethod(timelineType).Invoke(stack, null);
                    ModEntry.WriteLog("[navigate_menu] PushSubmenuType<NTimelineScreen>");
                    return new { success = true, target, invoked = "PushSubmenuType<NTimelineScreen>" };
                }

                case "multiplayer":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    var mpType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu, sts2");
                    if (mpType == null)
                        return new { error = "Could not find NMultiplayerSubmenu type" };

                    var pushMethod = stack.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "PushSubmenuType" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    if (pushMethod == null)
                        return new { error = "Could not find PushSubmenuType method" };

                    pushMethod.MakeGenericMethod(mpType).Invoke(stack, null);
                    ModEntry.WriteLog("[navigate_menu] PushSubmenuType<NMultiplayerSubmenu>");
                    return new { success = true, target, invoked = "PushSubmenuType<NMultiplayerSubmenu>" };
                }

                case "new_run" or "new_game" or "singleplayer":
                {
                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    // v0.101.0+: MainMenu → NSingleplayerSubmenu → CharacterSelect
                    // Use OpenSingleplayerSubmenu (public) to get the submenu, then
                    // invoke OpenCharacterSelect (private) to push through to character select
                    var openSubMethod = mainMenu.GetType().GetMethod("OpenSingleplayerSubmenu",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (openSubMethod != null)
                    {
                        var submenu = openSubMethod.Invoke(mainMenu, null);
                        if (submenu != null)
                        {
                            var charSelectMethod = submenu.GetType().GetMethod("OpenCharacterSelect",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (charSelectMethod != null)
                            {
                                charSelectMethod.Invoke(submenu, new object?[] { null });
                                ModEntry.WriteLog("[navigate_menu] OpenSingleplayerSubmenu → OpenCharacterSelect");
                                return new { success = true, target, invoked = "OpenSingleplayerSubmenu+OpenCharacterSelect" };
                            }
                        }
                        // Submenu opened but couldn't push to character select — still usable
                        ModEntry.WriteLog("[navigate_menu] OpenSingleplayerSubmenu (stopped at submenu)");
                        return new { success = true, target, invoked = "OpenSingleplayerSubmenu" };
                    }

                    // Fallback for older game versions: call SingleplayerButtonPressed directly
                    var fallback = mainMenu.GetType().GetMethod("SingleplayerButtonPressed",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fallback == null)
                        return new { error = "Could not find SingleplayerButtonPressed or OpenSingleplayerSubmenu" };

                    fallback.Invoke(mainMenu, new object?[] { null });
                    ModEntry.WriteLog("[navigate_menu] Invoked SingleplayerButtonPressed (fallback)");
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
                    // First check if there's a popup overlay (e.g. NErrorPopup) — dismiss it
                    var screenObj = GetActiveScreenObject();
                    if (screenObj != null)
                    {
                        var screenTypeName = screenObj.GetType().Name;
                        if (screenTypeName.Contains("Popup") || screenTypeName.Contains("Error") || screenTypeName.Contains("Dialog"))
                        {
                            // Try OnOkButtonPressed(NButton) or OnCancelButtonPressed(NButton)
                            if (TryInvokeMethod(screenObj,
                                    ["OnOkButtonPressed", "OnCancelButtonPressed", "Close", "Dismiss"],
                                    [null],
                                    out var dismissMethod))
                            {
                                ModEntry.WriteLog($"[navigate_menu] Dismissed popup via {dismissMethod} on {screenTypeName}");
                                return new { success = true, target, invoked = dismissMethod, screen_type = screenTypeName };
                            }
                            // Fallback: QueueFree the popup node
                            if (screenObj is Godot.Node popupNode)
                            {
                                popupNode.QueueFree();
                                ModEntry.WriteLog($"[navigate_menu] QueueFree'd popup {screenTypeName}");
                                return new { success = true, target, invoked = "QueueFree", screen_type = screenTypeName };
                            }
                        }
                    }

                    if (mainMenu == null)
                        return new { error = "Not on main menu" };

                    var stackProp = mainMenu.GetType().GetProperty("SubmenuStack",
                        BindingFlags.Public | BindingFlags.Instance);
                    var stack = stackProp?.GetValue(mainMenu);
                    if (stack == null)
                        return new { error = "Could not access SubmenuStack" };

                    // Check if stack has anything to pop
                    var submenusOpenProp = stack.GetType().GetProperty("SubmenusOpen",
                        BindingFlags.Public | BindingFlags.Instance);
                    var submenusOpen = submenusOpenProp?.GetValue(stack) as bool? ?? true;
                    if (!submenusOpen)
                        return new { error = "Already on main menu (submenu stack is empty)" };

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

                    // Try common proceed/continue patterns (0-arg methods)
                    if (TryInvokeMethod(screenObj, ["OpenSummaryScreen", "Proceed", "Continue", "Confirm", "Done", "Close", "Leave", "Accept", "Dismiss"], Array.Empty<object?>(), out var invokedMethod))
                    {
                        ModEntry.WriteLog($"[navigate_menu] proceed via {invokedMethod} on {screenObj.GetType().Name}");
                        return new { success = true, target, invoked = invokedMethod, screen_type = screenObj.GetType().Name };
                    }

                    // Try popup dismiss methods that take an NButton parameter (e.g. OnOkButtonPressed(NButton _))
                    if (TryInvokeMethod(screenObj, ["OnOkButtonPressed", "OnCancelButtonPressed", "OnCloseButtonPressed", "OnDismissButtonPressed"], [null], out invokedMethod))
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
                    return new { error = $"Unknown target: {target}. Valid: continue, compendium, card_library, settings, profile, timeline, multiplayer, new_run, abandon, back, proceed" };
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

    private static object StartAutoRotate()
    {
        ModEntry.WriteLog("[AutoRotate] start_auto_rotate is deprecated; forwarding to foil tilt.");
        return StartFoilTilt();
    }

    private static object StopAutoRotate()
    {
        ModEntry.WriteLog("[AutoRotate] stop_auto_rotate is deprecated; forwarding to foil tilt.");
        return StopFoilTilt();
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
            ResetCardTilt(ctrl);
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

    private static int _refreshCounter = 0;

    private static void RefreshCardList()
    {
        var tree = GodotEngine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        _knownCardIds.Clear();
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
                if (body == null || portrait == null || !portrait.Visible)
                {
                    ResetCardTilt(card);
                    continue;
                }

                var rect = body.GetGlobalRect();
                if (rect.Size.X < 1 || rect.Size.Y < 1) continue;

                float halfWidth = body.Size.X > 1f ? body.Size.X * 0.5f : 150f;
                float halfHeight = body.Size.Y > 1f ? body.Size.Y * 0.5f : 211f;

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
                float tiltAngle = rel.X * FoilMaxTilt * prox;
                float tiltRad = tiltAngle * Mathf.Pi / 180.0f;
                float targetScaleX = Mathf.Cos(tiltRad);
                float targetPivotX = Mathf.Clamp(halfWidth - tiltAngle * 3.0f, 0f, halfWidth * 2.0f);

                float currentPivotX = body.PivotOffset.X == 0f ? halfWidth : body.PivotOffset.X;
                float currentPivotY = body.PivotOffset.Y == 0f ? halfHeight : body.PivotOffset.Y;
                float newScaleX = Mathf.Lerp(body.Scale.X, targetScaleX, FoilTiltLerp);
                float newPivotX = Mathf.Lerp(currentPivotX, targetPivotX, FoilTiltLerp);
                float newPivotY = Mathf.Lerp(currentPivotY, halfHeight, FoilTiltLerp);

                body.PivotOffset = new Vector2(newPivotX, newPivotY);
                body.Scale = new Vector2(newScaleX, 1.0f);
            }
            catch { }
        }
    }

    private static void ResetCardTilt(Control card)
    {
        card.PivotOffset = new Vector2(150f, 211f);
        card.Scale = Vector2.One;

        var body = card.GetNodeOrNull<Control>("%CardContainer");
        if (body == null)
            return;

        float halfWidth = body.Size.X > 1f ? body.Size.X * 0.5f : 150f;
        float halfHeight = body.Size.Y > 1f ? body.Size.Y * 0.5f : 211f;

        body.PivotOffset = new Vector2(halfWidth, halfHeight);
        body.Scale = Vector2.One;
    }

    // ─── FMOD Audio Test ────────────────────────────────────────────────────

    private static GodotObject? _fmodServer;
    private static GodotObject? GetFmodServer()
    {
        if (_fmodServer != null) return _fmodServer;
        try { _fmodServer = GodotEngine.GetSingleton("FmodServer"); }
        catch { }
        return _fmodServer;
    }

    private static object FmodTest(JsonElement root)
    {
        var results = new List<string>();
        string action = "probe";
        if (root.TryGetProperty("params", out var p))
        {
            if (p.TryGetProperty("action", out var actionProp))
                action = actionProp.GetString() ?? "probe";
        }

        try
        {
            // Step 1: Get FmodServer singleton
            GodotObject? fmodServer = null;
            try
            {
                fmodServer = GodotEngine.GetSingleton("FmodServer");
                results.Add($"FmodServer singleton: {fmodServer?.GetClass() ?? "null"}");
            }
            catch (Exception ex)
            {
                results.Add($"FmodServer singleton FAILED: {ex.Message}");
                return new { success = false, results, error = "Cannot access FmodServer" };
            }

            if (fmodServer == null)
                return new { success = false, results, error = "FmodServer is null" };

            if (action == "probe")
            {
                // List interesting methods
                var methods = fmodServer.GetMethodList();
                var methodNames = new List<string>();
                foreach (var method in methods)
                {
                    var name = method["name"].AsString();
                    if (name.Contains("play") || name.Contains("load") || name.Contains("bank") ||
                        name.Contains("sound") || name.Contains("music") || name.Contains("event") ||
                        name.Contains("create") || name.Contains("file"))
                    {
                        methodNames.Add(name);
                    }
                }
                results.Add($"Found {methods.Count} total methods, {methodNames.Count} audio-related");
                return new { success = true, results, audio_methods = methodNames };
            }

            if (action == "play_existing")
            {
                // Play an existing FMOD event directly via FmodServer
                string eventPath = "event:/sfx/heal";
                if (root.TryGetProperty("params", out var pp) && pp.TryGetProperty("event", out var evProp))
                    eventPath = evProp.GetString() ?? eventPath;

                try
                {
                    fmodServer.Call("play_one_shot", eventPath);
                    results.Add($"play_one_shot('{eventPath}') succeeded!");
                    return new { success = true, results, @event = eventPath };
                }
                catch (Exception ex)
                {
                    results.Add($"play_one_shot failed: {ex.Message}");

                    // Try create_event_instance approach
                    try
                    {
                        var instance = fmodServer.Call("create_event_instance", eventPath);
                        results.Add($"create_event_instance returned: {instance}");
                        if (instance.Obj is GodotObject fmodEvent)
                        {
                            fmodEvent.Call("start");
                            fmodEvent.Call("release");
                            results.Add("Event started via create_event_instance!");
                            return new { success = true, results, @event = eventPath, method = "create_event_instance" };
                        }
                    }
                    catch (Exception ex2)
                    {
                        results.Add($"create_event_instance also failed: {ex2.Message}");
                    }
                    return new { success = false, results };
                }
            }

            if (action == "load_file")
            {
                // Try to load a custom audio file via FmodServer
                string filePath = "";
                if (root.TryGetProperty("params", out var pp2) && pp2.TryGetProperty("path", out var pathProp))
                    filePath = pathProp.GetString() ?? "";

                if (string.IsNullOrEmpty(filePath))
                    return new { success = false, error = "params.path required" };

                results.Add($"Attempting load_file_as_sound: {filePath}");
                try
                {
                    var result = fmodServer.Call("load_file_as_sound", filePath);
                    results.Add($"load_file_as_sound returned: {result} (type: {result.VariantType})");
                    return new { success = true, results, loaded = filePath };
                }
                catch (Exception ex)
                {
                    results.Add($"load_file_as_sound failed: {ex.Message}");

                    // Try load_file_as_music
                    try
                    {
                        var result = fmodServer.Call("load_file_as_music", filePath);
                        results.Add($"load_file_as_music returned: {result} (type: {result.VariantType})");
                        return new { success = true, results, loaded = filePath, method = "load_file_as_music" };
                    }
                    catch (Exception ex2)
                    {
                        results.Add($"load_file_as_music also failed: {ex2.Message}");
                    }

                    return new { success = false, results };
                }
            }

            if (action == "play_fmod_file")
            {
                // Load a custom audio file and play it through FMOD
                string filePath = "";
                if (root.TryGetProperty("params", out var pp3) && pp3.TryGetProperty("path", out var pathProp))
                    filePath = pathProp.GetString() ?? "";

                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                    return new { success = false, error = $"File not found: {filePath}" };

                try
                {
                    // Step 1: Load file into FMOD
                    results.Add($"Loading file: {filePath}");
                    var fmodFile = fmodServer.Call("load_file_as_sound", filePath);
                    results.Add($"load_file_as_sound returned: {fmodFile} (type: {fmodFile.VariantType})");

                    // Step 2: Try create_sound_instance with the file path
                    try
                    {
                        results.Add("Trying create_sound_instance with file path...");
                        var soundInstance = fmodServer.Call("create_sound_instance", filePath);
                        results.Add($"create_sound_instance returned: {soundInstance} (type: {soundInstance.VariantType})");

                        if (soundInstance.Obj is GodotObject sndObj)
                        {
                            // Try to play it - check what methods the sound instance has
                            var methods = sndObj.GetMethodList();
                            var methodNames = new List<string>();
                            foreach (var m in methods)
                            {
                                var mName = m["name"].AsString();
                                if (mName.Contains("play") || mName.Contains("start") || mName.Contains("volume") ||
                                    mName.Contains("set") || mName.Contains("release") || mName.Contains("stop") ||
                                    mName.Contains("get") || mName.Contains("is_"))
                                    methodNames.Add(mName);
                            }
                            results.Add($"Sound instance methods: {string.Join(", ", methodNames)}");

                            // Try playing
                            try
                            {
                                sndObj.Call("play");
                                results.Add("play() succeeded!");
                                return new { success = true, results, method = "create_sound_instance" };
                            }
                            catch (Exception ex)
                            {
                                results.Add($"play() failed: {ex.Message}");
                            }

                            try
                            {
                                sndObj.Call("start");
                                results.Add("start() succeeded!");
                                return new { success = true, results, method = "create_sound_instance+start" };
                            }
                            catch (Exception ex)
                            {
                                results.Add($"start() failed: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add($"create_sound_instance failed: {ex.Message}");
                    }

                    // Step 3: Try creating a programmer instrument event instance
                    // and seeing if it picks up the loaded file
                    try
                    {
                        results.Add("Checking if any event uses programmer instrument...");
                        var allEvents = fmodServer.Call("get_all_event_descriptions");
                        results.Add($"get_all_event_descriptions returned {allEvents.VariantType}");
                    }
                    catch (Exception ex)
                    {
                        results.Add($"get_all_event_descriptions failed: {ex.Message}");
                    }

                    return new { success = false, results, note = "File loaded into FMOD but playback method not yet found" };
                }
                catch (Exception ex)
                {
                    return new { success = false, error = ex.Message, results };
                }
            }

            if (action == "list_buses")
            {
                try
                {
                    var busResults = new List<object>();
                    string[] busPaths = { "bus:/", "bus:/master", "bus:/master/sfx", "bus:/master/music", "bus:/master/ambience" };
                    foreach (var busPath in busPaths)
                    {
                        try
                        {
                            var bus = fmodServer.Call("get_bus", busPath);
                            busResults.Add(new { path = busPath, type = bus.VariantType.ToString(), obj = bus.ToString() });
                        }
                        catch (Exception ex)
                        {
                            busResults.Add(new { path = busPath, error = ex.Message });
                        }
                    }
                    return new { success = true, buses = busResults };
                }
                catch (Exception ex)
                {
                    return new { success = false, error = ex.Message };
                }
            }

            if (action == "test_all")
            {
                var testResults = new List<object>();
                var wav = "";
                if (root.TryGetProperty("params", out var pp4) && pp4.TryGetProperty("path", out var wavProp))
                    wav = wavProp.GetString() ?? "";

                // ── Test 1: PlayEvent (play_one_shot) ──
                try
                {
                    fmodServer.Call("play_one_shot", "event:/sfx/heal");
                    testResults.Add(new { test = "PlayEvent", status = "PASS", detail = "event:/sfx/heal" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "PlayEvent", status = "FAIL", detail = ex.Message });
                }

                System.Threading.Thread.Sleep(300);

                // ── Test 2: PlayEvent with params ──
                try
                {
                    var dict = new Godot.Collections.Dictionary();
                    dict["EnemyImpact_Intensity"] = 2f;
                    fmodServer.Call("play_one_shot_with_params", "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_base", dict);
                    testResults.Add(new { test = "PlayEventWithParams", status = "PASS", detail = "enemy_impact with intensity=2" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "PlayEventWithParams", status = "FAIL", detail = ex.Message });
                }

                System.Threading.Thread.Sleep(300);

                // ── Test 3: PlayFile (load_file_as_sound + create_sound_instance + play) ──
                if (!string.IsNullOrEmpty(wav) && System.IO.File.Exists(wav))
                {
                    try
                    {
                        fmodServer.Call("load_file_as_sound", wav);
                        var snd = fmodServer.Call("create_sound_instance", wav).Obj as GodotObject;
                        snd?.Call("set_volume", 0.8f);
                        snd?.Call("set_pitch", 1.2f);
                        snd?.Call("play");

                        bool playing = snd != null && (bool)snd.Call("is_playing");
                        float vol = snd != null ? snd.Call("get_volume").AsSingle() : -1;
                        float pitch = snd != null ? snd.Call("get_pitch").AsSingle() : -1;

                        testResults.Add(new { test = "PlayFile", status = "PASS",
                            detail = $"playing={playing}, vol={vol:F2}, pitch={pitch:F2}" });

                        System.Threading.Thread.Sleep(400);
                        snd?.Call("stop");
                        snd?.Call("release");
                    }
                    catch (Exception ex)
                    {
                        testResults.Add(new { test = "PlayFile", status = "FAIL", detail = ex.Message });
                    }
                }
                else
                {
                    testResults.Add(new { test = "PlayFile", status = "SKIP", detail = "No wav path provided" });
                }

                // ── Test 4: PlayMusic (load_file_as_music + create_sound_instance) ──
                if (!string.IsNullOrEmpty(wav) && System.IO.File.Exists(wav))
                {
                    try
                    {
                        // Unload the sound version first, reload as music (streaming)
                        try { fmodServer.Call("unload_file", wav); } catch { }

                        fmodServer.Call("load_file_as_music", wav);
                        var snd = fmodServer.Call("create_sound_instance", wav).Obj as GodotObject;
                        snd?.Call("play");
                        bool playing = snd != null && (bool)snd.Call("is_playing");
                        testResults.Add(new { test = "PlayMusic(streaming)", status = "PASS",
                            detail = $"playing={playing}" });

                        System.Threading.Thread.Sleep(300);
                        snd?.Call("stop");
                        snd?.Call("release");
                        try { fmodServer.Call("unload_file", wav); } catch { }
                    }
                    catch (Exception ex)
                    {
                        testResults.Add(new { test = "PlayMusic(streaming)", status = "FAIL", detail = ex.Message });
                    }
                }
                else
                {
                    testResults.Add(new { test = "PlayMusic(streaming)", status = "SKIP", detail = "No wav path" });
                }

                // ── Test 5: CreateEventInstance (looping / controllable) ──
                try
                {
                    var inst = fmodServer.Call("create_event_instance", "event:/sfx/buff").Obj as GodotObject;
                    inst?.Call("set_volume", 0.7f);
                    inst?.Call("start");
                    bool valid = inst != null && (bool)inst.Call("is_valid");
                    testResults.Add(new { test = "CreateEventInstance", status = "PASS",
                        detail = $"valid={valid}" });
                    System.Threading.Thread.Sleep(300);
                    inst?.Call("stop", 1);
                    inst?.Call("release");
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "CreateEventInstance", status = "FAIL", detail = ex.Message });
                }

                // ── Test 6: Snapshots ──
                try
                {
                    var snap = fmodServer.Call("create_event_instance", "snapshot:/pause").Obj as GodotObject;
                    snap?.Call("start");
                    testResults.Add(new { test = "StartSnapshot", status = "PASS", detail = "snapshot:/pause started" });
                    System.Threading.Thread.Sleep(500);
                    snap?.Call("stop", 0); // allow fadeout
                    snap?.Call("release");
                    testResults.Add(new { test = "StopSnapshot", status = "PASS", detail = "snapshot stopped with fadeout" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "Snapshots", status = "FAIL", detail = ex.Message });
                }

                System.Threading.Thread.Sleep(300);

                // ── Test 7: Bus control ──
                try
                {
                    var sfxBus = fmodServer.Call("get_bus", "bus:/master/sfx").Obj as GodotObject;
                    float origVol = sfxBus != null ? sfxBus.Call("get_volume").AsSingle() : -1;

                    // Temporarily lower SFX bus volume
                    sfxBus?.Call("set_volume", 0.1f);
                    float lowVol = sfxBus != null ? sfxBus.Call("get_volume").AsSingle() : -1;

                    // Play a sound at low bus volume
                    fmodServer.Call("play_one_shot", "event:/sfx/debuff");
                    System.Threading.Thread.Sleep(300);

                    // Restore
                    sfxBus?.Call("set_volume", origVol);
                    float restoredVol = sfxBus != null ? sfxBus.Call("get_volume").AsSingle() : -1;

                    testResults.Add(new { test = "BusVolume", status = "PASS",
                        detail = $"orig={origVol:F3} → low={lowVol:F3} → restored={restoredVol:F3}" });

                    // Test bus mute
                    sfxBus?.Call("set_mute", true);
                    fmodServer.Call("play_one_shot", "event:/sfx/buff"); // should be silent
                    System.Threading.Thread.Sleep(200);
                    sfxBus?.Call("set_mute", false);
                    testResults.Add(new { test = "BusMute", status = "PASS", detail = "muted and unmuted SFX bus" });

                    // Test other buses exist
                    var busNames = new[] { "bus:/master", "bus:/master/music", "bus:/master/ambience",
                        "bus:/master/sfx/Reverb", "bus:/master/sfx/chorus" };
                    var foundBuses = new List<string>();
                    foreach (var bn in busNames)
                    {
                        try
                        {
                            var b = fmodServer.Call("get_bus", bn).Obj as GodotObject;
                            if (b != null) foundBuses.Add(bn);
                        }
                        catch { }
                    }
                    testResults.Add(new { test = "BusExists", status = "PASS",
                        detail = $"Found {foundBuses.Count}/{busNames.Length}: {string.Join(", ", foundBuses.Select(b => b.Split('/').Last()))}" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "BusControl", status = "FAIL", detail = ex.Message });
                }

                // ── Test 8: Global parameters ──
                try
                {
                    float origProgress = fmodServer.Call("get_global_parameter_by_name", "Progress").AsSingle();

                    // Temporarily change progress
                    fmodServer.Call("set_global_parameter_by_name", "sfx_duck", 0.5f);
                    float duckVal = fmodServer.Call("get_global_parameter_by_name", "sfx_duck").AsSingle();
                    fmodServer.Call("set_global_parameter_by_name", "sfx_duck", 0f); // restore

                    testResults.Add(new { test = "GlobalParameters", status = "PASS",
                        detail = $"Progress={origProgress:F0}, sfx_duck set to {duckVal:F2} and restored" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "GlobalParameters", status = "FAIL", detail = ex.Message });
                }

                // ── Test 9: SetGlobalParameterByLabel ──
                try
                {
                    fmodServer.Call("set_global_parameter_by_name_with_label", "sfx_duck", "0");
                    testResults.Add(new { test = "GlobalParamByLabel", status = "PASS", detail = "set_global_parameter_by_name_with_label worked" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "GlobalParamByLabel", status = "FAIL", detail = ex.Message });
                }

                // ── Test 10: Mute/Unmute all ──
                try
                {
                    fmodServer.Call("mute_all_events");
                    fmodServer.Call("play_one_shot", "event:/sfx/heal"); // should be silent
                    System.Threading.Thread.Sleep(200);
                    fmodServer.Call("unmute_all_events");
                    testResults.Add(new { test = "MuteUnmuteAll", status = "PASS", detail = "muted, played (silent), unmuted" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "MuteUnmuteAll", status = "FAIL", detail = ex.Message });
                }

                // ── Test 11: Pause/Unpause all ──
                try
                {
                    fmodServer.Call("pause_all_events");
                    System.Threading.Thread.Sleep(200);
                    fmodServer.Call("unpause_all_events");
                    testResults.Add(new { test = "PauseUnpauseAll", status = "PASS", detail = "paused and unpaused" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "PauseUnpauseAll", status = "FAIL", detail = ex.Message });
                }

                // ── Test 12: EventExists / BusExists ──
                try
                {
                    bool healExists = fmodServer.Call("check_event_path", "event:/sfx/heal").AsBool();
                    bool fakeExists = fmodServer.Call("check_event_path", "event:/sfx/totally_fake_event").AsBool();
                    bool sfxBusExists = fmodServer.Call("check_bus_path", "bus:/master/sfx").AsBool();
                    bool fakeBusExists = fmodServer.Call("check_bus_path", "bus:/fake_bus").AsBool();

                    testResults.Add(new { test = "EventExists", status = healExists && !fakeExists ? "PASS" : "FAIL",
                        detail = $"heal={healExists}, fake={fakeExists}" });
                    testResults.Add(new { test = "BusExists", status = sfxBusExists && !fakeBusExists ? "PASS" : "FAIL",
                        detail = $"sfx={sfxBusExists}, fake={fakeBusExists}" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "ExistsChecks", status = "FAIL", detail = ex.Message });
                }

                // ── Test 13: DSP buffer settings ──
                try
                {
                    int bufLen = fmodServer.Call("get_system_dsp_buffer_length").AsInt32();
                    int bufCount = fmodServer.Call("get_system_dsp_num_buffers").AsInt32();
                    testResults.Add(new { test = "DspBufferSettings", status = "PASS",
                        detail = $"length={bufLen}, count={bufCount}" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "DspBufferSettings", status = "FAIL", detail = ex.Message });
                }

                // ── Test 14: Performance data ──
                try
                {
                    var perf = fmodServer.Call("get_performance_data");
                    testResults.Add(new { test = "PerformanceData", status = "PASS",
                        detail = $"type={perf.VariantType}, value={perf.ToString()?.Substring(0, Math.Min(200, perf.ToString()?.Length ?? 0))}" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "PerformanceData", status = "FAIL", detail = ex.Message });
                }

                // ── Test 15: PlayEventByGuid ──
                try
                {
                    // Get the GUID for the heal event
                    var guid = fmodServer.Call("get_event_guid", "event:/sfx/heal");
                    fmodServer.Call("play_one_shot_using_guid", guid.AsString());
                    testResults.Add(new { test = "PlayEventByGuid", status = "PASS",
                        detail = $"guid={guid}" });
                }
                catch (Exception ex)
                {
                    testResults.Add(new { test = "PlayEventByGuid", status = "FAIL", detail = ex.Message });
                }

                // ── Summary ──
                int passed = testResults.Count(r => r.GetType().GetProperty("status")?.GetValue(r)?.ToString() == "PASS");
                int failed = testResults.Count(r => r.GetType().GetProperty("status")?.GetValue(r)?.ToString() == "FAIL");
                int skipped = testResults.Count(r => r.GetType().GetProperty("status")?.GetValue(r)?.ToString() == "SKIP");

                // Final confirmation sound
                System.Threading.Thread.Sleep(200);
                fmodServer.Call("play_one_shot", "event:/sfx/npcs/merchant/merchant_thank_yous");

                return new { success = failed == 0, passed, failed, skipped, total = testResults.Count, tests = testResults };
            }

            return new { success = false, error = $"Unknown action: {action}. Use: probe, play_existing, load_file, play_fmod_file, list_buses, test_all" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message, results };
        }
    }
}
