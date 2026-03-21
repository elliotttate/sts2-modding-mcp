# BaseLib: CustomRelicModel

Extends `RelicModel` with automatic registration and ID management.

## Base Class
```csharp
using Alchyr.Sts2.BaseLib.Relics;

public sealed class MyRelic : CustomRelicModel
{
    // All standard RelicModel overrides work here
}
```

## Key Differences from RelicModel
- **Auto-add to content dictionary**: Registered automatically — no manual `ModelDb` setup
- **ID prefixing**: `ICustomModel` prefix applied automatically to prevent collisions with base game IDs
- **Pool compatibility**: Works with `CustomRelicPoolModel` for character-specific pools

## Pool Registration
```csharp
// Shared pool (all characters can find it)
[Pool(typeof(SharedRelicPool))]
public sealed class MyRelic : CustomRelicModel { ... }

// Character-specific pool
[Pool(typeof(IroncladRelicPool))]
public sealed class MyRelic : CustomRelicModel { ... }

// Custom pool using BaseLib
[Pool(typeof(MyRelicPool))]
public sealed class MyRelic : CustomRelicModel { ... }

public class MyRelicPool : CustomRelicPoolModel { }
```

## Standard Overrides
All `RelicModel` overrides still apply:
- `Rarity` — Common, Uncommon, Rare, Boss, Event, Starter, Special
- `MaxCharges` / `InitialCharges` — for counter-based relics
- Hook methods: `BeforeCombatStart`, `AfterCardPlayed`, `AfterTurnEnd`, `OnPickUp`, etc.
- `Flash()` — visual activation indicator

## Image Requirements
- `images/relics/MY_RELIC.png` — 128×128 main icon
- `images/relics/MY_RELIC_packed.png` — 64×64 packed icon
- Pack into your mod's PCK at `res://YourMod/images/relics/`

## When to Use
Use `CustomRelicModel` instead of `RelicModel` when your mod uses BaseLib. Saves registration boilerplate and prevents ID conflicts.
