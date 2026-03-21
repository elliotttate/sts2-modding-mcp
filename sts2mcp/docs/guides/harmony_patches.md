# Harmony Patches

## Overview
Harmony patches let you modify game methods at runtime without changing game files.
The game ships with Harmony 2.4.2 (`0Harmony.dll`).

## Patch Types
- **Prefix**: Runs BEFORE original method. Return `false` to skip original.
- **Postfix**: Runs AFTER original method. Can modify `__result`.
- **Transpiler**: Modify IL code directly (advanced).

## Syntax
```csharp
[HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
public static class MyPatch
{
    // Prefix - can prevent original from running
    public static bool Prefix(TargetClass __instance, ref ReturnType __result) { ... }

    // Postfix - runs after, can modify result
    public static void Postfix(TargetClass __instance, ref ReturnType __result) { ... }
}
```

## Special Parameters
- `__instance` - The object the method is called on
- `__result` - Return value (ref in postfix to modify)
- `__state` - Pass data from prefix to postfix
- Parameter names matching original method parameters

## Accessing Private Fields
- `Traverse.Create(__instance).Field("_fieldName").GetValue<Type>()`
- `AccessTools.FieldRefAccess<Type, FieldType>("fieldName")`

## Manual Patching
```csharp
var harmony = new Harmony("my.mod.id");
harmony.PatchAll(); // Auto-discover all [HarmonyPatch] classes
// or manually:
harmony.Patch(originalMethod, new HarmonyMethod(prefixMethod));
```

## Common Targets
- `CardModel.PortraitPath` (getter) - Replace card images
- `MonsterModel.CreateVisuals` - Custom monster visuals
- `CombatManager.SetReadyToEndTurn` - End turn events
- `NGame._Input` - Custom keybindings
- `NGame.LaunchMainMenu` - Skip splash screen
- Act `.GenerateAllEncounters` - Add encounters
