# BaseLib: Utility Classes

## GodotUtils
- `CreatureVisualsFromScene(path)` - Load scene as NCreatureVisuals (no CreateVisuals patch needed!)
- `TransferAllNodes(from, to)` - Move all children between nodes

## ShaderUtils
- `GenerateHsv(hue, sat, val)` - Create HSV shader material for color-shifted sprites

## GeneratedNodePool<T>
Object pooling for Godot nodes:
```csharp
var pool = new GeneratedNodePool<MyNode>(() => new MyNode());
pool.Initialize(preWarmCount: 5);
var node = pool.Get();
pool.Return(node);  // cleans up signals automatically
```

## Extension Methods
- `type.GetPrefix()` - Get mod ID prefix from namespace
- `dynamicVar.CalculateBlock()` - Calculate block with all modifiers
- `dynamicVar.WithTooltip()` - Add hover tooltip
- `harmony.PatchAsyncMoveNext()` - Patch async state machines
- `control.DrawDebug()` - Debug UI rectangles
- `float.OrFast()` - Apply FastMode speed multipliers
- `valueProp.IsPoweredAttack_()` - Check ValueProp flags
