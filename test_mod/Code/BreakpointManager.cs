using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace MCPTest;

/// <summary>
/// Provides debugger-style breakpoints and stepping for the MCP bridge.
///
/// Two pause mechanisms depending on context:
///   1. Action-level: Uses ActionExecutor.Pause()/Unpause() — game keeps rendering,
///      just stops processing actions. Used for step-action and action breakpoints.
///   2. Hook-level: Uses ManualResetEventSlim to block the main thread at a specific
///      hook invocation. Game freezes but allows full state inspection.
///
/// Step modes:
///   - StepAction: Pause after each action completes
///   - StepTurn: Pause at the start of each player turn (BeforePlayPhaseStart)
///   - StepHook: Pause before each hook fires (expensive, for deep debugging)
/// </summary>
public static class BreakpointManager
{
    // ─── State ───────────────────────────────────────────────────────────────

    private static readonly object Lock = new();
    private static readonly List<Breakpoint> _breakpoints = new();
    private static int _nextBpId = 1;

    private static volatile bool _paused;
    private static ManualResetEventSlim? _hookResumeEvent;
    private static volatile StepMode _stepMode = StepMode.None;
    private static volatile bool _stepPending; // true = we owe the user a pause at next opportunity

    // Snapshot captured when we hit a breakpoint / step
    private static BreakpointContext? _currentContext;

    // Reference to ActionExecutor for pause/unpause (set via HookActionExecutor)
    private static object? _actionExecutorRef;
    private static System.Reflection.MethodInfo? _pauseMethod;
    private static System.Reflection.MethodInfo? _unpauseMethod;

    // ─── Enums ───────────────────────────────────────────────────────────────

    public enum StepMode
    {
        None,       // Free running
        Action,     // Pause after each game action
        Turn,       // Pause at start of each player turn
    }

    public enum BreakpointType
    {
        Action,     // Break when a specific action type executes
        Hook,       // Break when a specific hook fires
        Condition,  // Break when a condition is met (HP, gold, etc.)
    }

    // ─── Models ──────────────────────────────────────────────────────────────

    public class Breakpoint
    {
        public int Id { get; init; }
        public BreakpointType Type { get; init; }
        public string Target { get; init; } = "";   // Action type name or hook name
        public bool Enabled { get; set; } = true;
        public int HitCount { get; set; }
        public string? Condition { get; set; }       // Optional: "hp<10", "gold>500"
    }

    public class BreakpointContext
    {
        public string Location { get; set; } = "";       // Where we stopped
        public string Reason { get; set; } = "";          // Why we stopped
        public int? BreakpointId { get; set; }            // Which BP triggered (null if step)
        public string? ActionType { get; set; }           // Current action type
        public string? ActionDetail { get; set; }         // Action details
        public string? HookName { get; set; }             // Hook that fired
        public Dictionary<string, object?> GameState { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    // ─── Initialization ──────────────────────────────────────────────────────

    /// <summary>
    /// Called from ModEntry or BridgeHandler when combat starts to hook the ActionExecutor.
    /// </summary>
    public static void HookActionExecutor(object actionExecutor)
    {
        _actionExecutorRef = actionExecutor;
        // Cache MethodInfo once per executor type (won't change between combats)
        if (_pauseMethod == null)
        {
            var type = actionExecutor.GetType();
            _pauseMethod = type.GetMethod("Pause");
            _unpauseMethod = type.GetMethod("Unpause");
            if (_pauseMethod == null)
                ModEntry.WriteLog("BreakpointManager: WARNING — Pause() method not found on ActionExecutor");
        }
        ModEntry.WriteLog("BreakpointManager: Hooked ActionExecutor");
    }

    // ─── Breakpoint Management ───────────────────────────────────────────────

    public static Breakpoint AddBreakpoint(BreakpointType type, string target, string? condition = null)
    {
        lock (Lock)
        {
            var bp = new Breakpoint
            {
                Id = _nextBpId++,
                Type = type,
                Target = target,
                Condition = condition,
            };
            _breakpoints.Add(bp);
            ModEntry.WriteLog($"Breakpoint #{bp.Id} added: {type} on '{target}'" +
                (condition != null ? $" when {condition}" : ""));
            return bp;
        }
    }

    public static bool RemoveBreakpoint(int id)
    {
        lock (Lock)
        {
            var bp = _breakpoints.FirstOrDefault(b => b.Id == id);
            if (bp == null) return false;
            _breakpoints.Remove(bp);
            ModEntry.WriteLog($"Breakpoint #{id} removed");
            return true;
        }
    }

    public static List<Breakpoint> ListBreakpoints()
    {
        lock (Lock) { return _breakpoints.ToList(); }
    }

    public static void ClearAllBreakpoints()
    {
        lock (Lock) { _breakpoints.Clear(); }
        _stepMode = StepMode.None;
        _stepPending = false;
        ModEntry.WriteLog("All breakpoints cleared");
    }

    // ─── Step Mode ───────────────────────────────────────────────────────────

    public static void SetStepMode(StepMode mode)
    {
        _stepMode = mode;
        _stepPending = mode != StepMode.None;
        ModEntry.WriteLog($"Step mode: {mode}");
    }

    public static StepMode GetStepMode() => _stepMode;

    // ─── Pause / Resume ──────────────────────────────────────────────────────

    public static bool IsPaused => _paused;

    public static BreakpointContext? GetCurrentContext() => _currentContext;

    /// <summary>
    /// Pause action processing (non-blocking, game keeps rendering).
    /// Called from bridge command.
    /// </summary>
    public static void PauseActions()
    {
        if (_paused) return;
        _paused = true;

        TryPauseActionExecutor();
        _currentContext = CaptureContext("manual_pause", "Paused by user");
        ModEntry.WriteLog("BreakpointManager: Paused");
        EventTracker.Record("debug_pause", "Manual pause");
    }

    /// <summary>
    /// Resume from any pause (action-level or hook-level).
    /// NOTE: Does NOT use MainThreadDispatcher — hook breakpoints block the main thread,
    /// so dispatching there would deadlock. This runs directly on the TCP handler thread.
    /// </summary>
    public static void Resume()
    {
        if (!_paused) return;

        // If we're blocked at a hook-level breakpoint, signal it to continue.
        // The main thread (in HitHookBreakpoint) owns disposal of the event —
        // we only call Set() here. HitHookBreakpoint handles cleanup after
        // Wait() returns (it sets _paused = false and _currentContext = null).
        var evt = _hookResumeEvent;
        if (evt != null)
        {
            evt.Set();
            ModEntry.WriteLog("BreakpointManager: Resumed (hook breakpoint signaled)");
            EventTracker.Record("debug_resume", "Resumed from hook breakpoint");
            return;
        }

        // Action-level pause: unpause the executor and clear state
        TryUnpauseActionExecutor();

        _paused = false;
        _currentContext = null;
        ModEntry.WriteLog("BreakpointManager: Resumed");
        EventTracker.Record("debug_resume", "Resumed");
    }

    /// <summary>
    /// Step: resume, then immediately set step-pending so we pause at next opportunity.
    /// </summary>
    public static void Step()
    {
        _stepPending = true;
        Resume();
    }

    // ─── Check Points (called from Harmony patches) ─────────────────────────

    /// <summary>
    /// Called before an action executes. Checks action breakpoints and step-action mode.
    /// This runs on the main thread.
    /// </summary>
    public static void OnBeforeAction(GameAction action)
    {
        if (_paused) return; // Already paused (volatile read)

        var actionName = action.GetType().Name;

        lock (Lock)
        {
            if (_paused) return; // Double-check under lock

            // Check step mode
            if (_stepMode == StepMode.Action && _stepPending)
            {
                HitBreakpoint(null, "step_action", $"Stepped to action: {actionName}", actionName, null);
                return;
            }

            // Check action breakpoints
            var bp = _breakpoints.FirstOrDefault(b =>
                b.Enabled && b.Type == BreakpointType.Action &&
                actionName.Contains(b.Target, StringComparison.OrdinalIgnoreCase));
            if (bp != null && EvaluateCondition(bp.Condition))
            {
                bp.HitCount++;
                HitBreakpoint(bp.Id, "action_breakpoint",
                    $"Action breakpoint #{bp.Id} hit: {actionName}", actionName, null);
            }
        }
    }

    /// <summary>
    /// Called when a hook fires. Checks hook breakpoints and step-turn mode.
    /// This runs on the main thread.
    /// </summary>
    public static void OnHookFired(string hookName)
    {
        if (_paused) return; // Volatile read fast-path

        lock (Lock)
        {
            if (_paused) return; // Double-check under lock

            // Step-turn: pause at BeforePlayPhaseStart
            if (_stepMode == StepMode.Turn && _stepPending &&
                hookName.Equals("BeforePlayPhaseStart", StringComparison.OrdinalIgnoreCase))
            {
                HitHookBreakpoint(null, "step_turn", $"Stepped to turn start", hookName);
                return;
            }

            // Check hook breakpoints
            var bp = _breakpoints.FirstOrDefault(b =>
                b.Enabled && b.Type == BreakpointType.Hook &&
                hookName.Equals(b.Target, StringComparison.OrdinalIgnoreCase));
            if (bp != null && EvaluateCondition(bp.Condition))
            {
                bp.HitCount++;
                HitHookBreakpoint(bp.Id, "hook_breakpoint",
                    $"Hook breakpoint #{bp.Id} hit: {hookName}", hookName);
            }
        }
    }

    // ─── Internal ────────────────────────────────────────────────────────────

    private static void HitBreakpoint(int? bpId, string location, string reason,
        string? actionType, string? hookName)
    {
        _paused = true;
        _stepPending = false;
        TryPauseActionExecutor();

        _currentContext = CaptureContext(location, reason);
        _currentContext.BreakpointId = bpId;
        _currentContext.ActionType = actionType;
        _currentContext.HookName = hookName;

        ModEntry.WriteLog($"BREAKPOINT: {reason}");
        EventTracker.Record("breakpoint_hit", reason, new Dictionary<string, object?>
        {
            ["location"] = location,
            ["breakpoint_id"] = bpId,
            ["action_type"] = actionType,
            ["hook_name"] = hookName,
        });
    }

    private static void HitHookBreakpoint(int? bpId, string location, string reason, string hookName)
    {
        _paused = true;
        _stepPending = false;

        _currentContext = CaptureContext(location, reason);
        _currentContext.BreakpointId = bpId;
        _currentContext.HookName = hookName;

        ModEntry.WriteLog($"HOOK BREAKPOINT: {reason} — blocking main thread");
        EventTracker.Record("breakpoint_hit", reason, new Dictionary<string, object?>
        {
            ["location"] = location,
            ["breakpoint_id"] = bpId,
            ["hook_name"] = hookName,
            ["blocking"] = true,
        });

        // Block the main thread until resume is called (with safety timeout).
        // We hold a local ref so Resume() calling Set() on a disposed event is safe —
        // we null the field first, then dispose, preventing the race.
        var evt = new ManualResetEventSlim(false);
        _hookResumeEvent = evt;
        if (!evt.Wait(TimeSpan.FromMinutes(5)))
        {
            // Timeout: client likely disconnected — auto-resume to unfreeze the game
            ModEntry.WriteLog("HOOK BREAKPOINT TIMEOUT: No resume received in 5 minutes, auto-resuming");
            EventTracker.Record("breakpoint_timeout", $"Auto-resumed after 5min timeout at {hookName}");
        }
        // Clear field before disposing to prevent Resume() from calling Set() on disposed event
        _hookResumeEvent = null;
        evt.Dispose();
        _paused = false;
        _currentContext = null;
    }

    private static BreakpointContext CaptureContext(string location, string reason)
    {
        var ctx = new BreakpointContext
        {
            Location = location,
            Reason = reason,
        };

        try
        {
            // Capture game state snapshot
            var state = ctx.GameState;
            state["screen"] = ScreenDetector.GetCurrentScreen();

            var cm = CombatManager.Instance;
            state["in_combat"] = cm?.IsInProgress ?? false;
            state["is_player_turn"] = cm?.IsPlayPhase ?? false;

            if (cm?.IsInProgress == true)
            {
                var combatState = cm.DebugOnlyGetState();
                if (combatState != null)
                {
                    state["round"] = combatState.RoundNumber;

                    // Player state (allies are the player creatures)
                    var players = new List<Dictionary<string, object?>>();
                    foreach (var creature in combatState.Allies)
                    {
                        var player = creature.Player;
                        var pcs = player?.PlayerCombatState;
                        var playerDict = new Dictionary<string, object?>
                        {
                            ["hp"] = creature.CurrentHp,
                            ["max_hp"] = creature.MaxHp,
                            ["block"] = creature.Block,
                            ["energy"] = pcs?.Energy ?? 0,
                            ["hand_size"] = pcs?.Hand?.Cards?.Count ?? 0,
                            ["draw_pile"] = pcs?.DrawPile?.Cards?.Count ?? 0,
                            ["discard_pile"] = pcs?.DiscardPile?.Cards?.Count ?? 0,
                        };

                        // Hand cards
                        var hand = new List<string>();
                        if (pcs?.Hand?.Cards != null)
                        {
                            foreach (var card in pcs.Hand.Cards)
                                hand.Add(card.GetType().Name);
                        }
                        playerDict["hand"] = hand;

                        // Powers
                        var powers = new List<Dictionary<string, object?>>();
                        foreach (var pw in creature.Powers)
                        {
                            powers.Add(new Dictionary<string, object?>
                            {
                                ["name"] = pw.GetType().Name,
                                ["amount"] = pw.Amount,
                            });
                        }
                        playerDict["powers"] = powers;

                        players.Add(playerDict);
                    }
                    state["players"] = players;

                    // Enemy state
                    var enemies = new List<Dictionary<string, object?>>();
                    foreach (var e in combatState.Enemies)
                    {
                        enemies.Add(new Dictionary<string, object?>
                        {
                            ["name"] = e.Monster?.GetType().Name ?? e.GetType().Name,
                            ["hp"] = e.CurrentHp,
                            ["max_hp"] = e.MaxHp,
                            ["block"] = e.Block,
                            ["intent"] = e.Monster?.NextMove?.GetType().Name,
                        });
                    }
                    state["enemies"] = enemies;
                }
            }

            if (RunManager.Instance?.IsInProgress == true)
            {
                var runState = RunManager.Instance.DebugOnlyGetState();
                if (runState != null)
                {
                    state["floor"] = runState.TotalFloor;
                    state["act"] = runState.CurrentActIndex + 1;
                    state["room"] = runState.CurrentRoom?.GetType().Name;
                }
            }

            // Current action info
            if (cm?.IsInProgress == true)
            {
                try
                {
                    var executor = _actionExecutorRef;
                    if (executor != null)
                    {
                        var currentProp = executor.GetType().GetProperty("CurrentlyRunningAction");
                        var current = currentProp?.GetValue(executor) as GameAction;
                        if (current != null)
                        {
                            ctx.ActionType = current.GetType().Name;
                            ctx.ActionDetail = current.ToString();
                        }
                    }
                }
                catch (Exception ex) { ModEntry.WriteLog($"CaptureContext action info error: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            ctx.GameState["capture_error"] = ex.Message;
        }

        return ctx;
    }

    private static bool EvaluateCondition(string? condition)
    {
        if (string.IsNullOrEmpty(condition)) return true;

        try
        {
            // Simple condition evaluator: "hp<10", "gold>500", "energy==0", "round>=3"
            var cm = CombatManager.Instance;
            if (cm?.IsInProgress != true) return true;
            var combatState = cm.DebugOnlyGetState();
            if (combatState == null) return true;

            var allies = combatState.Allies.ToList();
            if (allies.Count == 0) return true;
            var creature = allies[0];
            var player = creature.Player;
            var pcs = player?.PlayerCombatState;

            // Parse condition: field operator value
            string[] ops = { "<=", ">=", "!=", "==", "<", ">" };
            foreach (var op in ops)
            {
                var idx = condition.IndexOf(op, StringComparison.Ordinal);
                if (idx < 0) continue;

                var field = condition.Substring(0, idx).Trim().ToLowerInvariant();
                var valStr = condition.Substring(idx + op.Length).Trim();
                if (!decimal.TryParse(valStr, out var expected))
                {
                    ModEntry.WriteLog($"Condition parse error: cannot parse '{valStr}' as number in '{condition}'");
                    return false; // Malformed condition — don't fire the breakpoint
                }

                decimal actual = field switch
                {
                    "hp" => creature.CurrentHp,
                    "max_hp" => creature.MaxHp,
                    "block" => creature.Block,
                    "energy" => pcs?.Energy ?? 0,
                    "hand" or "hand_size" => pcs?.Hand?.Cards?.Count ?? 0,
                    "round" or "turn" => combatState.RoundNumber,
                    "gold" => (decimal)(RunManager.Instance.DebugOnlyGetState()?.Players[0]?.Gold ?? 0),
                    _ => 0,
                };

                return op switch
                {
                    "<" => actual < expected,
                    ">" => actual > expected,
                    "<=" => actual <= expected,
                    ">=" => actual >= expected,
                    "==" => actual == expected,
                    "!=" => actual != expected,
                    _ => true,
                };
            }

            // No operator matched — condition is malformed
            ModEntry.WriteLog($"Condition parse error: no valid operator found in '{condition}'");
            return false;
        }
        catch (Exception ex)
        {
            ModEntry.WriteLog($"Condition eval error: {ex.Message}");
            return false; // Don't fire breakpoint on eval failure
        }
    }

    private static void TryPauseActionExecutor()
    {
        try
        {
            if (_actionExecutorRef == null || _pauseMethod == null) return;
            _pauseMethod.Invoke(_actionExecutorRef, null);
        }
        catch (Exception ex) { ModEntry.WriteLog($"PauseActionExecutor error: {ex.Message}"); }
    }

    private static void TryUnpauseActionExecutor()
    {
        try
        {
            if (_actionExecutorRef == null || _unpauseMethod == null) return;
            _unpauseMethod.Invoke(_actionExecutorRef, null);
        }
        catch (Exception ex) { ModEntry.WriteLog($"UnpauseActionExecutor error: {ex.Message}"); }
    }
}
