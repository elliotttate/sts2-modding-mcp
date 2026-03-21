# BaseLib: CustomPotionModel

Extends `PotionModel` with automatic registration and pool management.

## Base Class
```csharp
using Alchyr.Sts2.BaseLib.Potions;

public sealed class MyPotion : CustomPotionModel
{
    // All standard PotionModel overrides work here
}
```

## Key Differences from PotionModel
- **AutoAdd property**: Defaults to `true` — automatically registered in pools without manual setup
- **ID prefixing**: `ICustomModel` prefix applied automatically to prevent base game ID collisions
- **Simplified pool registration**: Works out of the box with `SharedPotionPool` and character-specific pools

## Pool Registration
```csharp
// Available to all characters
[Pool(typeof(SharedPotionPool))]
public sealed class MyPotion : CustomPotionModel { ... }

// Character-specific
[Pool(typeof(IroncladPotionPool))]
public sealed class MyPotion : CustomPotionModel { ... }
```

## Standard Overrides
All `PotionModel` overrides still apply:
- `Rarity` — PotionRarity.Common, Uncommon, Rare
- `Usage` — PotionUsage.CombatOnly, OutOfCombat, Anywhere
- `TargetType` — None, AnyEnemy, AnyAlly, AnyPlayer, AllEnemies, AllAllies
- `OnUse(PlayerChoiceContext, Creature?)` — main potion effect

## Localization
```json
{
  "MY_POTION.title": "Potion Name",
  "MY_POTION.description": "Gain {amount} [gold]Block[/gold]."
}
```

## When to Use
Use `CustomPotionModel` instead of `PotionModel` when your mod uses BaseLib. The `AutoAdd` feature eliminates manual pool registration code.
