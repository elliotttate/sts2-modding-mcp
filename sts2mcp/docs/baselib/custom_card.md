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
- **Custom portrait paths**: Override `CustomPortraitPath`, `PortraitPath`, and `BetaPortraitPath`

## Constructor
```csharp
// Primary constructor pattern (recommended):
public sealed class MyCard(int cost, CardType type, CardRarity rarity, TargetType target)
    : CustomCardModel(cost, type, rarity, target)
{
    // autoAdd defaults to true (auto-registers with ModelDb)
    // showInCardLibrary defaults to true
}
```

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

## ConstructedCardModel (Fluent Builder)

For cards with many variables, keywords, or tags, `ConstructedCardModel` provides a fluent API:

```csharp
public sealed class MyCard : ConstructedCardModel
{
    public MyCard() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
        WithDamage(8)
            .WithBlock(5)
            .WithPower<VulnerablePower>(2)
            .WithKeywords(MyKeyword.CustomType)
            .WithTip(HoverTipFactory.FromPower<VulnerablePower>());
    }
}
```

**Available builder methods** (all return `this` for chaining):
- `WithVars(params DynamicVar[])` — add dynamic variables
- `WithVar(name, baseVal)` — add simple named variable
- `WithDamage(baseVal)` — add DamageVar
- `WithBlock(baseVal)` — add BlockVar
- `WithCards(baseVal)` — add card count variable
- `WithPower<T>(baseVal)` — add PowerVar with auto-tooltip
- `WithCalculatedVar(name, baseVal, bonus)` — variable calculated as baseVal + bonus
- `WithCalculatedBlock(baseVal, bonus)` / `WithCalculatedDamage(baseVal, bonus)`
- `WithKeywords(params CardKeyword[])` — add keywords
- `WithTags(params CardTag[])` — add tags
- `WithTip(IHoverTip)` — add hover tip
- `WithEnergyTip()` — add energy cost tip

## When to Use
Use `CustomCardModel` instead of `CardModel` when your mod uses BaseLib. It reduces boilerplate and handles registration automatically. Use `ConstructedCardModel` when you want the fluent builder pattern for complex cards. If you don't use BaseLib, extend `CardModel` directly and register manually.
