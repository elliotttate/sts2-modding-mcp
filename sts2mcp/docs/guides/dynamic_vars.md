# Creating Custom Dynamic Variables

## What Are DynamicVars?
DynamicVars are named numeric values used in card, power, and enchantment descriptions. When you write `{move}` in a localization string, the game resolves it to the current value of a `MoveVar` at display time. This lets descriptions update dynamically as the value changes (e.g., from power buffs, upgrades, or Focus).

## How They Work
1. A card/power/enchantment declares `CanonicalVars` — its list of DynamicVars
2. Localization strings reference them by name: `Deal {move} damage`
3. At render time, the game resolves each `{varName}` to the var's current value
4. Values can be modified by powers, relics, and game state

## Built-in DynamicVars
The game provides several standard vars:

| Class | Name | Typical Use |
|-------|------|------------|
| `MoveVar` | `move` | Damage amount, generic numeric |
| `BlockVar` | `block` | Block amount (also triggers block card frame) |
| `MagicVar` | `magic` | Secondary value (power stacks, draw count) |
| `UrMagicVar` | `urMagic` | Tertiary value |

## Creating a Custom DynamicVar
When built-in vars aren't enough (e.g., you need a 4th or 5th value, or a var with special resolution logic):

```csharp
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

public class BurnVar : DynamicVar
{
    public BurnVar(decimal baseValue, ValueProp valueProp = ValueProp.Move)
        : base("burn", baseValue, valueProp)
    {
    }
}
```

- First constructor arg (`"burn"`) is the name used in localization: `{burn}`
- `baseValue` is the default numeric value
- `ValueProp` determines which modification pipeline applies (Move for damage-like, Block for block-like)

## Using in CanonicalVars
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new MoveVar(8),       // {move} = 8
    new BurnVar(3),       // {burn} = 3
};
```

## Referencing in Localization
```json
{
  "MY_CARD.description": "Deal {move} damage. Apply {burn} [red]Burn[/red]."
}
```

The values update in real-time as powers modify them. For example, if the player has Strength, `{move}` increases automatically because it uses `ValueProp.Move`.

## ValueProp Options
| ValueProp | Modified By |
|-----------|------------|
| `Move` | Strength, damage multipliers, Weak |
| `Block` | Dexterity, block multipliers, Frail |
| `None` | Not modified by any power (fixed value) |

Choose the ValueProp that matches how the value should interact with the game's modifier system.

## Upgrade Changes
DynamicVars are part of upgrade logic:
```csharp
public override IEnumerable<UpgradeChange> GetUpgradeChanges()
{
    yield return UpgradeChange.ChangeVar<MoveVar>(12);   // {move} becomes 12
    yield return UpgradeChange.ChangeVar<BurnVar>(5);    // {burn} becomes 5
}
```

## Generator
Use `generate_dynamic_var` with a class name, var name, and default value.
