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

## Common OpCodes
- `Ldc_I4` / `Ldc_I4_S` - Load integer constant
- `Callvirt` / `Call` - Method calls
- `Ldfld` / `Stfld` - Field access
- `Ldarg_0` - Load `this`
- `Ret` - Return
