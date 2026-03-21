# BaseLib: IL Patching Utilities

## InstructionMatcher
Fluent builder for matching IL instruction sequences in Harmony transpilers:

```csharp
var matcher = new InstructionMatcher()
    .Ldarg_0()
    .Call(typeof(SomeClass).GetMethod("SomeMethod"))
    .Stloc(2);

var patcher = new InstructionPatcher(instructions);
if (patcher.Match(matcher))
{
    patcher.Replace(new[] {
        // replacement instructions
    });
}
```

## InstructionPatcher Methods
- `Match()` / `MatchStart()` / `MatchEnd()` - Find instruction patterns
- `Step(n)` - Move cursor position
- `Replace()` / `ReplaceLastMatch()` - Replace matched instructions
- `Insert()` / `InsertCopy()` - Insert new instructions
- `GetLabels()` / `GetOperandLabel()` - Extract IL labels
- `PrintLog()` / `PrintResult()` - Debug output

## PatchAsyncMoveNext Extension
For patching async methods (common in STS2):
```csharp
harmony.PatchAsyncMoveNext(
    typeof(CombatManager).GetMethod("StartTurn"),
    transpiler: new HarmonyMethod(typeof(MyPatch).GetMethod("Transpiler"))
);
```
