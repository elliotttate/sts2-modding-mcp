# Creating Custom Potions

## Base Class: PotionModel
Potions are single-use consumable items. Override key properties:

### Required Properties
- `Rarity` — PotionRarity.Common, Uncommon, Rare
- `Usage` — When the potion can be used:
  - `PotionUsage.CombatOnly` — Only during combat (most combat potions)
  - `PotionUsage.AnyTime` — Usable both in and out of combat (healing, utility)
  - `PotionUsage.Automatic` — Auto-triggered (e.g. Fairy in a Bottle)
- `TargetType` — Who/what the potion targets:
  - `PotionTargetType.None` — No target needed (self-buff, AoE)
  - `PotionTargetType.AnyEnemy` — Must select an enemy
  - `PotionTargetType.AnyAlly` — Must select an ally (multiplayer)
  - `PotionTargetType.AnyPlayer` — Must select a player
  - `PotionTargetType.AllEnemies` — Hits all enemies automatically
  - `PotionTargetType.AllAllies` — Affects all allies

### Main Effect Method
```csharp
public override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)
{
    // target is the selected creature (null if TargetType is None/All*)
    await choiceContext.Player.GainBlock(12);
}
```

## Pool Registration
```csharp
// Available to all characters
[Pool(typeof(SharedPotionPool))]
public sealed class MyPotion : PotionModel { ... }

// Character-specific
[Pool(typeof(IroncladPotionPool))]
public sealed class MyPotion : PotionModel { ... }
```

Available potion pools: `SharedPotionPool`, `IroncladPotionPool`, `SilentPotionPool`, `RegentPotionPool`, `NecrobinderPotionPool`, `DefectPotionPool`.

## DynamicVars for Descriptions
Use `CanonicalVars` for numeric values that appear in the description:
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new MoveVar(12),  // Referenced as {move} in localization
};
```

## Localization (potions.json)
```json
{
  "MY_POTION.title": "Iron Brew",
  "MY_POTION.description": "Gain {move} [gold]Block[/gold]."
}
```

Rich text tags: `[gold]`, `[blue]`, `[red]`, `[green]`, `[keyword]` for colored text.

## Common Potion Patterns

### Damage Potion (targeted)
```csharp
public override PotionTargetType TargetType => PotionTargetType.AnyEnemy;

public override async Task OnUse(PlayerChoiceContext ctx, Creature? target)
{
    await target!.TakeDamage(ctx, 20, DamageType.Normal);
}
```

### Self-Buff Potion (no target)
```csharp
public override PotionTargetType TargetType => PotionTargetType.None;

public override async Task OnUse(PlayerChoiceContext ctx, Creature? target)
{
    await ctx.Player.ApplyPower<StrengthPower>(3);
}
```

### AoE Potion (all enemies)
```csharp
public override PotionTargetType TargetType => PotionTargetType.AllEnemies;

public override async Task OnUse(PlayerChoiceContext ctx, Creature? target)
{
    foreach (var enemy in CombatState.Monsters)
        await enemy.ApplyPower<PoisonPower>(6);
}
```

## Console Test
```
potion MY_POTION
```

## BaseLib Alternative
Use `CustomPotionModel` from BaseLib for auto-registration. See `get_baselib_reference` topic `custom_potion`.
