# Advanced Harmony Patching

## IL Transpilers
Modify method bytecode directly — more powerful than prefix/postfix:
```csharp
[HarmonyPatch(typeof(TargetClass), "TargetMethod")]
public static class MyTranspiler
{
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = new List<CodeInstruction>(instructions);
        for (int i = 0; i < codes.Count; i++)
        {
            // Find target instruction
            if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 4)
            {
                // Replace constant 4 with 8
                codes[i].operand = 8;
                break;
            }
        }
        return codes;
    }
}
```

## BaseLib's InstructionMatcher/InstructionPatcher
```csharp
return new InstructionPatcher(instructions)
    .Match(new InstructionMatcher()
        .Opcode(OpCodes.Callvirt)
        .MethodName("GetResultPileType"))
    .InsertAfter(CodeInstruction.Call(typeof(MyPatch), nameof(Override)))
    .Finish();
```

## Async Method Patching
```csharp
// Patch async state machine's MoveNext method:
harmony.PatchAsyncMoveNext(
    typeof(TargetClass), "AsyncMethod",
    postfix: new HarmonyMethod(typeof(MyPatch), nameof(Postfix)));

// Async postfix for sequential card play:
[HarmonyPostfix]
static async void PlayAfter(CardModel __instance, Task __result)
{
    await __result;  // Wait for original to complete
    await CardCmd.AutoPlay(context, stitchedCard, target);
}
```

## Multi-Method Patching
```csharp
[HarmonyPatch]
public static class MultiPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CombatRoom), "Enter");
        yield return AccessTools.Method(typeof(EventRoom), "Enter");
        yield return AccessTools.Method(typeof(TreasureRoom), "Enter");
    }

    static void Postfix(object __instance) => Log.Info($"Entered: {__instance.GetType().Name}");
}
```

## Safe Patching Wrapper
```csharp
public static void PatchSafe(Harmony harmony, Type type, string method,
    string patchMethod, bool isPrefix, string label)
{
    try
    {
        var original = AccessTools.Method(type, method);
        if (original == null) { Log.Warn($"[{label}] Method not found"); return; }
        var patch = new HarmonyMethod(typeof(MyPatches), patchMethod);
        if (isPrefix) harmony.Patch(original, prefix: patch);
        else harmony.Patch(original, postfix: patch);
    }
    catch (Exception ex) { Log.Warn($"[{label}] Patch failed: {ex.Message}"); }
}
```

## Prefix Return Control
```csharp
[HarmonyPrefix]
static bool BlockSave(ref bool __result)
{
    if (shouldBlock)
    {
        __result = false;
        return false;  // Skip original method
    }
    return true;  // Run original
}
```

## Async Deadlock Patterns

STS2 is heavily async — card effects, enemy turns, and room transitions all use `async Task`. When patching async code, be aware of these common deadlocks:

### Task.Yield() Deadlocks
`Task.Yield()` posts continuations to the ThreadPool. In certain contexts (headless testing, custom synchronization), these continuations never complete:
```csharp
// PROBLEM: This can deadlock if the SynchronizationContext doesn't process ThreadPool work
await Task.Yield();  // Posts continuation to ThreadPool, may never resume

// SOLUTION: Patch YieldAwaitable.YieldAwaiter.IsCompleted to short-circuit
[HarmonyPrefix]
[HarmonyPatch(typeof(YieldAwaitable.YieldAwaiter), "get_IsCompleted")]
static bool ForceYieldComplete(ref bool __result)
{
    __result = true;   // Makes await Task.Yield() complete immediately
    return false;
}
```

Use a volatile flag to control when yield suppression is active (e.g., only during EndTurn processing):
```csharp
public static volatile bool SuppressYield;

static bool IsCompletedPrefix(ref bool __result)
{
    if (SuppressYield) { __result = true; return false; }
    return true;  // Normal behavior when not suppressing
}

// Usage: wrap critical sections
SuppressYield = true;
try { PlayerCmd.EndTurn(player, canBackOut: false); }
finally { SuppressYield = false; }
```

### Cmd.Wait() Deadlocks
`Cmd.Wait(float)` creates a SceneTreeTimer for UI animation delays. In headless or testing contexts without a Godot SceneTree, this never completes:
```csharp
// PROBLEM: Status cards (Wound, Dazed) added via CardPileCmd.AddToCombatAndPreview()
// call Cmd.Wait(1f) for the preview animation — hangs in headless mode

// SOLUTION: Patch Cmd.Wait to return immediately
[HarmonyPrefix]
static bool CmdWaitPrefix(ref Task __result)
{
    __result = Task.CompletedTask;
    return false;
}
```

### InlineSynchronizationContext
For forcing async game code to run synchronously (e.g., in tests or tools), use a custom SynchronizationContext that executes continuations inline:
```csharp
internal class InlineSynchronizationContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback, object?)> _queue = new();
    private bool _executing;

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (_executing) { _queue.Enqueue((d, state)); return; }
        _executing = true;
        try
        {
            d(state);
            while (_queue.Count > 0) { var (cb, st) = _queue.Dequeue(); cb(st); }
        }
        finally { _executing = false; }
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public void Pump()  // Call after async operations to drain queued callbacks
    {
        while (_queue.Count > 0)
        {
            var (cb, st) = _queue.Dequeue();
            _executing = true;
            try { cb(st); } finally { _executing = false; }
        }
    }
}
```

Install it before running game code:
```csharp
var syncCtx = new InlineSynchronizationContext();
SynchronizationContext.SetSynchronizationContext(syncCtx);
// ... run async game code synchronously ...
syncCtx.Pump();  // Drain any remaining callbacks
```

## Common OpCodes
- `Ldc_I4` / `Ldc_I4_S` - Load integer constant
- `Callvirt` / `Call` - Method calls
- `Ldfld` / `Stfld` - Field access
- `Ldarg_0` - Load `this`
- `Ret` - Return
