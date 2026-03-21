using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.GameActions;

namespace MCPTest;

/// <summary>
/// Harmony patches that inject breakpoint/stepping checks at strategic points
/// in the game's execution flow.
/// </summary>
public static class DebugPatches
{
    // Cached reflection info for ActionExecutor extraction (resolved once)
    private static MemberInfo? _actionExecutorMember;
    private static bool _actionExecutorMemberResolved;
    private static object? _lastHookedExecutor;

    /// <summary>
    /// Patch CombatManager.StartTurn to grab the ActionExecutor reference.
    /// ActionExecutor lives on RunManager.Instance.ActionExecutor (not CombatManager).
    /// </summary>
    [HarmonyPatch]
    public static class ActionExecutorPatches
    {
        [HarmonyPatch(typeof(CombatManager), "StartTurn")]
        [HarmonyPrefix]
        public static void StartTurnPrefix()
        {
            try
            {
                // ActionExecutor is on RunManager, not CombatManager
                if (!_actionExecutorMemberResolved)
                {
                    _actionExecutorMemberResolved = true;
                    var rmType = typeof(MegaCrit.Sts2.Core.Runs.RunManager);
                    _actionExecutorMember =
                        (MemberInfo?)rmType.GetProperty("ActionExecutor",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? rmType.GetField("_actionExecutor",
                            BindingFlags.NonPublic | BindingFlags.Instance);

                    ModEntry.WriteLog(_actionExecutorMember != null
                        ? $"DebugPatches: Found ActionExecutor via {_actionExecutorMember.Name}"
                        : "DebugPatches: Could not find ActionExecutor on RunManager");
                }

                if (_actionExecutorMember == null) return;

                var runManager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
                if (runManager == null) return;

                object? executor = _actionExecutorMember switch
                {
                    PropertyInfo pi => pi.GetValue(runManager),
                    FieldInfo fi => fi.GetValue(runManager),
                    _ => null,
                };

                if (executor != null && !ReferenceEquals(executor, _lastHookedExecutor))
                {
                    _lastHookedExecutor = executor;
                    BreakpointManager.HookActionExecutor(executor);
                }
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"DebugPatches.StartTurn error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch GameAction.Execute to notify BreakpointManager before each action runs.
    /// NOTE: Only a prefix — Execute() is async, so a postfix fires when the Task is
    /// returned (immediately), not when the action finishes. Step-action mode works
    /// because Step() sets _stepPending before resuming, and the next OnBeforeAction
    /// prefix checks it.
    /// </summary>
    [HarmonyPatch(typeof(GameAction), nameof(GameAction.Execute))]
    public static class GameActionExecutePatch
    {
        [HarmonyPrefix]
        public static void Prefix(GameAction __instance)
        {
            try
            {
                if (__instance.State == GameActionState.WaitingForExecution)
                    BreakpointManager.OnBeforeAction(__instance);
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"DebugPatches.GameAction.Prefix error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch key Hook methods for hook-level breakpoints.
    /// We patch the most useful hooks — not all 80+.
    /// </summary>
    [HarmonyPatch]
    public static class HookPatches
    {
        // Helper to call OnHookFired with the method name
        private static void NotifyHook(string hookName)
        {
            try { BreakpointManager.OnHookFired(hookName); }
            catch (Exception ex) { ModEntry.WriteLog($"HookPatch error ({hookName}): {ex.Message}"); }
        }

        // ── Combat Flow ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeCombatStart")]
        [HarmonyPrefix]
        public static void BeforeCombatStart() => NotifyHook("BeforeCombatStart");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforePlayPhaseStart")]
        [HarmonyPrefix]
        public static void BeforePlayPhaseStart() => NotifyHook("BeforePlayPhaseStart");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeSideTurnStart")]
        [HarmonyPrefix]
        public static void BeforeSideTurnStart() => NotifyHook("BeforeSideTurnStart");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeTurnEnd")]
        [HarmonyPrefix]
        public static void BeforeTurnEnd() => NotifyHook("BeforeTurnEnd");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterTurnEnd")]
        [HarmonyPrefix]
        public static void AfterTurnEnd() => NotifyHook("AfterTurnEnd");

        // ── Card Play ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeCardPlayed")]
        [HarmonyPrefix]
        public static void BeforeCardPlayed() => NotifyHook("BeforeCardPlayed");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterCardPlayed")]
        [HarmonyPrefix]
        public static void AfterCardPlayed() => NotifyHook("AfterCardPlayed");

        // ── Damage ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeDamageReceived")]
        [HarmonyPrefix]
        public static void BeforeDamageReceived() => NotifyHook("BeforeDamageReceived");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterDamageReceived")]
        [HarmonyPrefix]
        public static void AfterDamageReceived() => NotifyHook("AfterDamageReceived");

        // ── Death ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeDeath")]
        [HarmonyPrefix]
        public static void BeforeDeath() => NotifyHook("BeforeDeath");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterDeath")]
        [HarmonyPrefix]
        public static void AfterDeath() => NotifyHook("AfterDeath");

        // ── Powers ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforePowerAmountChanged")]
        [HarmonyPrefix]
        public static void BeforePowerAmountChanged() => NotifyHook("BeforePowerAmountChanged");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterPowerAmountChanged")]
        [HarmonyPrefix]
        public static void AfterPowerAmountChanged() => NotifyHook("AfterPowerAmountChanged");

        // ── Block ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeBlockGained")]
        [HarmonyPrefix]
        public static void BeforeBlockGained() => NotifyHook("BeforeBlockGained");

        // ── Room / Map ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeRoomEntered")]
        [HarmonyPrefix]
        public static void BeforeRoomEntered() => NotifyHook("BeforeRoomEntered");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterRoomEntered")]
        [HarmonyPrefix]
        public static void AfterRoomEntered() => NotifyHook("AfterRoomEntered");

        // ── Potion ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforePotionUsed")]
        [HarmonyPrefix]
        public static void BeforePotionUsed() => NotifyHook("BeforePotionUsed");

        // ── Energy / Draw ──

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterEnergySpent")]
        [HarmonyPrefix]
        public static void AfterEnergySpent() => NotifyHook("AfterEnergySpent");

        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeHandDraw")]
        [HarmonyPrefix]
        public static void BeforeHandDraw() => NotifyHook("BeforeHandDraw");
    }
}
