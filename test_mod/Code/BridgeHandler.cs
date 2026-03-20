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
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Helpers;

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

            object? result = method switch
            {
                "ping" => new { status = "ok", mod = "MCPTest", version = "1.0.0",
                    game_running = true, run_in_progress = RunManager.Instance.IsInProgress,
                    in_combat = CombatManager.Instance?.IsInProgress ?? false },
                "get_run_state" => GetRunState(),
                "get_combat_state" => GetCombatState(),
                "get_player_state" => GetPlayerState(),
                "get_screen_state" => GetScreenState(),
                "console" => ExecuteConsoleCommand(cmdParam ?? ""),
                "start_run" => StartRun(root),
                _ => new { error = $"Unknown method: {method}" },
            };

            return JsonSerializer.Serialize(new { result, id }, JsonOpts);
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"HandleRequest error: {ex}");
            return JsonSerializer.Serialize(new { error = ex.Message, id = 0 }, JsonOpts);
        }
    }

    private static object GetRunState()
    {
        try
        {
            if (!RunManager.Instance.IsInProgress)
                return new { in_progress = false };

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
                });
            }

            return new
            {
                in_progress = true,
                act = state.CurrentActIndex + 1,
                floor = state.TotalFloor,
                act_floor = state.ActFloor,
                ascension = state.AscensionLevel,
                player_count = state.Players.Count,
                seed = state.Rng?.StringSeed ?? "unknown",
                current_room = state.CurrentRoom?.GetType().Name ?? "none",
                players,
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object GetCombatState()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsInProgress)
                return new { in_combat = false };

            var combatState = cm.DebugOnlyGetState();
            if (combatState == null)
                return new { in_combat = false };

            var enemies = new List<object>();
            foreach (var creature in combatState.Enemies)
            {
                var powers = new List<object>();
                foreach (var power in creature.Powers)
                {
                    powers.Add(new { name = power.GetType().Name, amount = power.Amount });
                }

                string? nextMove = null;
                try { nextMove = creature.Monster?.NextMove?.Id; } catch { }

                enemies.Add(new
                {
                    name = creature.Monster?.GetType().Name ?? "unknown",
                    hp = creature.CurrentHp,
                    max_hp = creature.MaxHp,
                    block = creature.Block,
                    is_alive = creature.IsAlive,
                    next_move = nextMove,
                    powers,
                });
            }

            var playerStates = new List<object>();
            foreach (var creature in combatState.Allies)
            {
                var player = creature.Player;
                if (player == null) continue;

                var hand = new List<object>();
                if (player.PlayerCombatState?.Hand?.Cards != null)
                {
                    foreach (var c in player.PlayerCombatState.Hand.Cards)
                    {
                        hand.Add(new
                        {
                            name = c.GetType().Name,
                            cost = c.EnergyCost.ToString(),
                            type = c.Type.ToString(),
                            upgraded = c.CurrentUpgradeLevel > 0,
                        });
                    }
                }

                var powers = new List<object>();
                foreach (var p in creature.Powers)
                {
                    powers.Add(new { name = p.GetType().Name, amount = p.Amount });
                }

                playerStates.Add(new
                {
                    character = player.Character?.GetType().Name,
                    hp = creature.CurrentHp,
                    max_hp = creature.MaxHp,
                    block = creature.Block,
                    energy = player.PlayerCombatState?.Energy ?? 0,
                    max_energy = player.PlayerCombatState?.MaxEnergy ?? 0,
                    hand_size = hand.Count,
                    hand,
                    draw_pile = player.PlayerCombatState?.DrawPile?.Cards.Count ?? 0,
                    discard_pile = player.PlayerCombatState?.DiscardPile?.Cards.Count ?? 0,
                    exhaust_pile = player.PlayerCombatState?.ExhaustPile?.Cards.Count ?? 0,
                    powers,
                });
            }

            return new
            {
                in_combat = true,
                round = combatState.RoundNumber,
                is_player_turn = cm.IsPlayPhase,
                enemy_count = enemies.Count,
                enemies,
                players = playerStates,
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object GetPlayerState()
    {
        try
        {
            if (!RunManager.Instance.IsInProgress)
                return new { error = "No run in progress" };

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
                return new { error = "No run state" };

            var players = new List<object>();
            foreach (var p in state.Players)
            {
                var deck = new List<object>();
                if (p.Deck?.Cards != null)
                {
                    foreach (var c in p.Deck.Cards)
                    {
                        deck.Add(new
                        {
                            name = c.GetType().Name,
                            type = c.Type.ToString(),
                            rarity = c.Rarity.ToString(),
                            cost = c.EnergyCost.ToString(),
                            upgraded = c.CurrentUpgradeLevel > 0,
                        });
                    }
                }

                var relics = new List<object>();
                if (p.Relics != null)
                {
                    foreach (var r in p.Relics)
                    {
                        relics.Add(new { name = r.GetType().Name, rarity = r.Rarity.ToString() });
                    }
                }

                var potions = new List<object>();
                for (int i = 0; i < p.MaxPotionCount; i++)
                {
                    try
                    {
                        var pot = p.Potions.ElementAtOrDefault(i);
                        potions.Add(pot != null
                            ? new { slot = i, name = pot.GetType().Name, rarity = pot.Rarity.ToString() }
                            : (object)new { slot = i, name = "empty" });
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
                    deck,
                    relics,
                    potions,
                });
            }

            return new { players };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object GetScreenState()
    {
        try
        {
            bool runInProgress = RunManager.Instance.IsInProgress;
            bool inCombat = CombatManager.Instance?.IsInProgress ?? false;
            string? currentRoom = null;

            if (runInProgress)
            {
                var state = RunManager.Instance.DebugOnlyGetState();
                currentRoom = state?.CurrentRoom?.GetType().Name;
            }

            return new
            {
                run_in_progress = runInProgress,
                in_combat = inCombat,
                is_play_phase = inCombat && (CombatManager.Instance?.IsPlayPhase ?? false),
                current_room = currentRoom,
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static object ExecuteConsoleCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new { error = "No command provided" };

        try
        {
            EnsureConsoleAccess();
            if (_devConsole == null || _processCommandMethod == null)
                return new { error = "DevConsole not available" };

            // Console commands that modify game state must run on the main thread
            bool dispatched = false;
            MainThreadDispatcher.Enqueue(() =>
            {
                try
                {
                    var cmdResult = _processCommandMethod!.Invoke(_devConsole, new object[] { command });
                    ModEntry.WriteLog($"Console (main thread): {command} => done");
                }
                catch (Exception ex2)
                {
                    ModEntry.WriteLog($"Console main thread error: {ex2.Message}");
                }
            });

            ModEntry.WriteLog($"Console dispatched: {command}");
            return new { success = true, command, dispatched = true };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"Console error: {ex}");
            return new { error = ex.Message, command };
        }
    }

    private static object StartRun(JsonElement root)
    {
        try
        {
            if (RunManager.Instance.IsInProgress)
                return new { error = "A run is already in progress" };

            string characterName = "Ironclad";
            if (root.TryGetProperty("params", out var p) && p.TryGetProperty("character", out var cProp))
                characterName = cProp.GetString() ?? "Ironclad";

            int ascension = 0;
            if (root.TryGetProperty("params", out var p2) && p2.TryGetProperty("ascension", out var aProp))
                ascension = aProp.GetInt32();

            // Find character from ModelDb.AllCharacters
            CharacterModel? charModel = null;
            foreach (var ch in ModelDb.AllCharacters)
            {
                if (ch.GetType().Name.Equals(characterName, StringComparison.OrdinalIgnoreCase))
                {
                    charModel = ch;
                    break;
                }
            }
            if (charModel == null)
            {
                var available = string.Join(", ", ModelDb.AllCharacters.Select(c => c.GetType().Name));
                return new { error = $"Character '{characterName}' not found. Available: {available}" };
            }

            // Get acts
            var acts = ModelDb.Acts.ToList();

            // Generate seed
            var seed = DateTime.Now.Ticks.ToString();

            // Call NGame.Instance.StartNewSingleplayerRun via reflection (it's async)
            var nGameType = Type.GetType("MegaCrit.Sts2.Core.Nodes.NGame, sts2");
            if (nGameType == null) return new { error = "NGame type not found" };

            var instanceProp = nGameType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var nGame = instanceProp?.GetValue(null);
            if (nGame == null) return new { error = "NGame.Instance is null" };

            var startMethod = nGameType.GetMethod("StartNewSingleplayerRun",
                BindingFlags.Public | BindingFlags.Instance);
            if (startMethod == null) return new { error = "StartNewSingleplayerRun method not found" };

            var emptyModifiers = new List<ModifierModel>();

            // MUST run on main thread - Godot scene operations crash from background threads
            MainThreadDispatcher.Enqueue(() =>
            {
                try
                {
                    var task = startMethod.Invoke(nGame, new object?[] {
                        charModel, true,
                        (IReadOnlyList<ActModel>)acts,
                        (IReadOnlyList<ModifierModel>)emptyModifiers,
                        seed, ascension, null
                    });
                    if (task is System.Threading.Tasks.Task t)
                    {
                        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(t);
                    }
                    ModEntry.WriteLog($"StartRun dispatched to main thread successfully");
                }
                catch (Exception ex2)
                {
                    ModEntry.WriteLog($"StartRun main thread error: {ex2}");
                }
            });

            ModEntry.WriteLog($"StartRun: character={characterName}, ascension={ascension}, seed={seed}");
            return new { success = true, character = characterName, ascension, seed };
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"StartRun error: {ex}");
            return new { error = ex.Message };
        }
    }

    private static void EnsureConsoleAccess()
    {
        if (_devConsole != null && _processCommandMethod != null) return;

        try
        {
            // NDevConsole has a private static _instance field and a private _devConsole field
            var nDevConsoleType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole, sts2");
            if (nDevConsoleType != null)
            {
                // Get the static _instance field
                var instanceField = nDevConsoleType.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var nDevConsole = instanceField?.GetValue(null);

                if (nDevConsole != null)
                {
                    ModEntry.WriteLog($"Found NDevConsole instance: {nDevConsole.GetType().Name}");

                    // Get the private _devConsole field
                    var consoleField = nDevConsoleType.GetField("_devConsole",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (consoleField != null)
                    {
                        _devConsole = consoleField.GetValue(nDevConsole);
                        ModEntry.WriteLog($"Found _devConsole: {_devConsole?.GetType().Name ?? "null"}");
                    }
                    else
                    {
                        ModEntry.WriteLog("_devConsole field not found on NDevConsole");
                    }
                }
                else
                {
                    ModEntry.WriteLog("NDevConsole._instance is null (console not created yet)");
                }
            }
            else
            {
                ModEntry.WriteLog("NDevConsole type not found");
            }

            if (_devConsole != null)
            {
                _processCommandMethod = _devConsole.GetType().GetMethod("ProcessCommand",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(string) }, null);
                ModEntry.WriteLog($"ProcessCommand method found: {_processCommandMethod != null}");
            }
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"EnsureConsoleAccess error: {ex}");
        }
    }
}
