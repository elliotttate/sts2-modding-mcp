# BaseLib: CustomCardModel

Extends `CardModel` with automatic registration, custom frames, and convenience features.

## Base Class
```csharp
using Alchyr.Sts2.BaseLib.Cards;

public sealed class MyCard : CustomCardModel
{
    // All standard CardModel overrides work here
}
```

## Key Differences from CardModel
- **Auto-registration**: Added to `CustomContentDictionary` automatically — no manual `ModelDb` registration needed
- **ID prefixing**: `ICustomModel` prefix applied automatically to avoid ID collisions with base game
- **GainsBlock auto-detection**: If your `CanonicalVars` includes a `BlockVar`, the card automatically gets the block frame/color
- **Custom frames**: Your pool's `FramePath`/`FrameMaterial` are used if set (via `CustomCardPoolModel`)

## Pool Registration
```csharp
using Alchyr.Sts2.BaseLib.Cards;

// Use CustomCardPoolModel for custom frame support
[Pool(typeof(MyCardPool))]
public sealed class MyCard : CustomCardModel { ... }

public class MyCardPool : CustomCardPoolModel
{
    // Optional: set FramePath / FrameMaterial for custom card frames
}
```

Or add to an existing game pool:
```csharp
[Pool(typeof(IroncladCardPool))]
public sealed class MyCard : CustomCardModel { ... }
```

## Standard Overrides
All `CardModel` overrides still apply:
- `CardType` — Attack, Skill, Power, Status, Curse
- `Rarity` — Common, Uncommon, Rare, Basic, Special
- `EnergyCost` — mana cost (decimal)
- `CanonicalVars` — DynamicVars for description values
- `OnPlay(PlayerChoiceContext)` — main card effect
- `GetUpgradeChanges()` — what changes on upgrade

## When to Use
Use `CustomCardModel` instead of `CardModel` when your mod uses BaseLib. It reduces boilerplate and handles registration automatically. If you don't use BaseLib, extend `CardModel` directly and register manually.
