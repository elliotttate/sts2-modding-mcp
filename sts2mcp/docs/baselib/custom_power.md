# BaseLib: CustomPowerModel + ICustomPower

Extends `PowerModel` with custom icon support and ID management.

## Base Class
```csharp
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models.Powers;

public sealed class MyPower : CustomPowerModel
{
    // All standard PowerModel overrides work here
}
```

## Key Differences from PowerModel
- **ID prefixing**: `ICustomModel` marker for automatic ID prefixing
- **Custom icons**: Implement `ICustomPower` interface for mod-specific power icons

## Custom Icons via ICustomPower
```csharp
public sealed class MyPower : CustomPowerModel, ICustomPower
{
    // 64×64 packed icon (shown in power bar)
    public string PackedIcon => "res://MyMod/images/powers/my_power_packed.png";

    // 256×256 full icon (shown in tooltips/inspect)
    public string BigIcon => "res://MyMod/images/powers/my_power.png";

    // Optional beta art variant
    public string? BigBetaIcon => null;
}
```

## Standard Overrides
All `PowerModel` overrides still apply:
- `StackType` — Intensity (stacks amount), Duration (counts down), NoneAndStacks, None
- `ModifyDamageAdditive(int)` — flat damage modification
- `ModifyDamageMultiplicative(float)` — multiplied damage modification
- `ModifyBlockAdditive(int)` — flat block modification
- `BeforeTurnStart()` / `AfterTurnEnd()` — turn lifecycle
- `OnApply()` / `OnRemove()` — stack lifecycle
- `TickDown()` — for Duration powers, decrements each turn

## Image Requirements
- `images/powers/MY_POWER.png` — 256×256 full icon
- `images/powers/MY_POWER_packed.png` — 64×64 packed icon
- Pack into PCK at `res://YourMod/images/powers/`

## When to Use
Use `CustomPowerModel` when your mod uses BaseLib and you want custom icons. Without BaseLib, extend `PowerModel` directly — icons will use defaults.
