# Pool System

## Overview
Pools control which entities (cards, relics, potions) appear for which characters during a run. Every card, relic, and potion must belong to at least one pool to appear in the game.

## How Pools Work
- Entities are assigned to pools via the `[Pool(typeof(PoolName))]` attribute on the class
- During run generation, the game collects all entities from pools relevant to the active character
- Shared pools (like `SharedRelicPool`) are included for every character
- Character-specific pools only contribute to their character's runs
- An entity can belong to multiple pools

## Card Pools
| Pool | Characters |
|------|-----------|
| `IroncladCardPool` | Ironclad only |
| `SilentCardPool` | Silent only |
| `RegentCardPool` | Regent only |
| `NecrobinderCardPool` | Necrobinder only |
| `DefectCardPool` | Defect only |
| `ColorlessCardPool` | All characters (colorless cards) |

```csharp
[Pool(typeof(IroncladCardPool))]
public sealed class MyCard : CardModel { ... }

// Card appears for multiple characters
[Pool(typeof(IroncladCardPool))]
[Pool(typeof(SilentCardPool))]
public sealed class SharedCard : CardModel { ... }
```

## Relic Pools
| Pool | Usage |
|------|-------|
| `SharedRelicPool` | Available to all characters |
| `IroncladRelicPool` | Ironclad only |
| `SilentRelicPool` | Silent only |
| `RegentRelicPool` | Regent only |
| `NecrobinderRelicPool` | Necrobinder only |
| `DefectRelicPool` | Defect only |
| `EventRelicPool` | Only obtainable from events |
| `FallbackRelicPool` | Used as fallback when main pool is exhausted |

## Potion Pools
| Pool | Usage |
|------|-------|
| `SharedPotionPool` | Available to all characters |
| `IroncladPotionPool` | Ironclad only |
| `SilentPotionPool` | Silent only |
| `RegentPotionPool` | Regent only |
| `NecrobinderPotionPool` | Necrobinder only |
| `DefectPotionPool` | Defect only |

## Programmatic Registration
If you can't use the `[Pool]` attribute (e.g., conditional registration), use `ModHelper`:
```csharp
// Must be called during mod initialization, BEFORE pools are frozen
ModHelper.AddModelToPool<SharedRelicPool, MyRelic>();
ModHelper.AddModelToPool<IroncladCardPool, MyCard>();
```

**Important**: Pools are frozen during game initialization. Any `AddModelToPool` calls must happen in your `[ModInitializer]` method, not later.

## Custom Pools (BaseLib)
BaseLib provides custom pool base classes for mod-specific pools:
- `CustomCardPoolModel` — supports custom card frames via H/S/V color, `DeckEntryCardColor`
- `CustomRelicPoolModel` — custom relic pool for character mods
- `CustomPotionPoolModel` — custom potion pool for character mods

All three are in `BaseLib.Abstracts`. The `[Pool]` attribute is in `BaseLib.Utils`.

```csharp
using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;

public class MyCharacterCardPool : CustomCardPoolModel
{
    public override string Title => "mychar";
    public override float H => 0.5f;   // Hue
    public override float S => 1f;     // Saturation
    public override float V => 1f;     // Value
    public override Color DeckEntryCardColor => new("ff6644");
    public override bool IsColorless => false;
    public override bool IsShared => false;
}

[Pool(typeof(MyCharacterCardPool))]
public sealed class MyCard : CustomCardModel { ... }
```

With BaseLib custom pools, cards auto-register via `[Pool]` — you do NOT need to override `GenerateAllCards()`.

## CRITICAL: Every Custom Entity Needs [Pool]

When using BaseLib's `CustomCardModel` / `CustomRelicModel` / `CustomPotionModel`, **every entity class MUST have a `[Pool]` attribute**. Without it, BaseLib throws a fatal runtime exception during game startup:
```
System.Exception: Model MyMod.MyCard must be marked with a PoolAttribute to determine which pool to add it to.
```

This applies to ALL card types including curses and status cards:
```csharp
[Pool(typeof(CurseCardPool))]    // Curses go in CurseCardPool
public sealed class MyCurse : CustomCardModel { ... }

[Pool(typeof(StatusCardPool))]   // Status cards go in StatusCardPool
public sealed class MyStatus : CustomCardModel { ... }
```

Powers (`CustomPowerModel`) do NOT need pool attributes — they are registered automatically.

## Pool and Rarity Interaction
Within a pool, entities are further filtered by rarity when the game generates rewards, shop items, or other offerings. Common items appear more frequently than Rare ones.

## Debugging Pools
- Use `list_entities` with `entity_type=card_pool` or `entity_type=relic_pool` to see all game pools
- Use `get_entity_source` on a pool class to see its members
- Use console commands to test: `card MY_CARD`, `relic MY_RELIC`, `potion MY_POTION`
