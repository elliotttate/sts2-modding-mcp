# Creating Custom Cards

## Base Class: CardModel
Cards extend `CardModel` and override key properties:
- `Type`: CardType.Attack, Skill, Power, Status, Curse
- `Rarity`: CardRarity.Basic, Common, Uncommon, Rare
- `TargetType`: AnyEnemy, AllEnemies, RandomEnemy, None, Self, AnyAlly, AllAllies
- `EnergyCost`: Integer energy cost
- `GainsBlock`: Override to `true` if the card gains block (enables block card frame)

## Pool Registration
Use `[Pool(typeof(PoolClass))]` attribute to control which characters can find this card:
```csharp
[Pool(typeof(IroncladCardPool))]
public sealed class MyCard : CardModel { ... }
```

Pools: `IroncladCardPool`, `SilentCardPool`, `RegentCardPool`, `NecrobinderCardPool`, `DefectCardPool`, `ColorlessCardPool` (all characters).

## Complete Example: Attack Card
```csharp
[Pool(typeof(IroncladCardPool))]
public sealed class FlameStrike : CardModel
{
    public override CardType Type => CardType.Attack;
    public override CardRarity Rarity => CardRarity.Common;
    public override TargetType TargetType => TargetType.AnyEnemy;
    public override CardEnergyCost EnergyCost => 2;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Move),
        new PowerVar<VulnerablePower>(1m),
    };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromPower<VulnerablePower>(),
    };

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);
        await PowerCmd.Apply<VulnerablePower>(
            cardPlay.Target, DynamicVars.Vulnerable.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4m);
        DynamicVars.Vulnerable.UpgradeValueBy(1m);
    }
}
```

## Complete Example: Skill Card (Block)
```csharp
[Pool(typeof(IroncladCardPool))]
public sealed class IronBarrier : CardModel
{
    public override CardType Type => CardType.Skill;
    public override CardRarity Rarity => CardRarity.Common;
    public override TargetType TargetType => TargetType.Self;
    public override CardEnergyCost EnergyCost => 1;
    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(8m, ValueProp.Move),
    };

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
    }
}
```

## Complete Example: Power Card
```csharp
[Pool(typeof(IroncladCardPool))]
public sealed class BattleFrenzy : CardModel
{
    public override CardType Type => CardType.Power;
    public override CardRarity Rarity => CardRarity.Uncommon;
    public override TargetType TargetType => TargetType.Self;
    public override CardEnergyCost EnergyCost => 1;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(2m),
    };

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<StrengthPower>(
            Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Strength.UpgradeValueBy(1m);
    }
}
```

## Dynamic Variables (CanonicalVars)
Override `CanonicalVars` to declare numeric values used in the card description and OnPlay:

| Var Class | Name in DynamicVars | Localization Key | Use |
|-----------|-------------------|-----------------|-----|
| `DamageVar(Xm, ValueProp.Move)` | `.Damage` | `{Damage}` | Attack damage (scales with Strength) |
| `BlockVar(Xm, ValueProp.Move)` | `.Block` | `{Block}` | Block amount (scales with Dexterity) |
| `MagicVar(Xm)` | `.MagicNumber` | `{MagicNumber}` | Generic number (draw, discard count) |
| `PowerVar<T>(Xm)` | `.PowerName` or `["PowerName"]` | `{PowerName}` | Power stack amount |

**Important**: Values use `m` suffix for decimal (`12m`, `5m`). `ValueProp.Move` makes the value scale with Strength/Dexterity.

### Accessing Vars in OnPlay
```csharp
DynamicVars.Damage.BaseValue     // decimal — use for DamageCmd
DynamicVars.Block                // BlockVar object — pass directly to CreatureCmd.GainBlock
DynamicVars["CustomPower"].BaseValue  // string key access for non-standard vars
```

Typed properties exist for common powers: `.Strength`, `.Vulnerable`, `.Weak`, `.Poison`, `.Dexterity`, `.Doom`, etc. For custom power vars, use the string indexer `DynamicVars["YourPowerName"]`.

## Commands Pattern (OnPlay)

### Dealing Damage
```csharp
// Basic targeted attack
await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
    .FromCard(this)
    .Targeting(cardPlay.Target)
    .Execute(choiceContext);

// With hit VFX
await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
    .FromCard(this)
    .Targeting(cardPlay.Target)
    .WithHitFx("vfx/vfx_attack_slash")
    .Execute(choiceContext);

// Using damage result for follow-up effects
AttackCommand attack = await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
    .FromCard(this)
    .Targeting(cardPlay.Target)
    .Execute(choiceContext);
int totalDamage = attack.Results.Sum(r => r.TotalDamage);
```

### Gaining Block
```csharp
await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
```

### Applying Powers
```csharp
// To an enemy
await PowerCmd.Apply<VulnerablePower>(cardPlay.Target, amount, Owner.Creature, this);

// To self
await PowerCmd.Apply<StrengthPower>(Owner.Creature, amount, Owner.Creature, this);
```

### Drawing Cards
```csharp
await CardPileCmd.Draw(Owner, (int)DynamicVars.MagicNumber.BaseValue, choiceContext);
```

### Discarding Cards
```csharp
await CardPileCmd.Discard(Owner, (int)DynamicVars.MagicNumber.BaseValue, choiceContext);
```

## Targeting Rules

When implementing `OnPlay`, understand how `TargetType` determines targeting:

| TargetType | `cardPlay.Target` | What to do |
|---|---|---|
| `AnyEnemy` | The selected enemy | Use `cardPlay.Target` directly |
| `AllEnemies` | `null` | Loop over all alive enemies |
| `RandomEnemy` | `null` | Game selects random target |
| `Self` | `null` | Use `Owner.Creature` |
| `AnyAlly` | The selected ally | Use `cardPlay.Target` |
| `AllAllies` | `null` | Loop over all allies |
| `None` | `null` | No target needed |

**Critical pitfall**: Self-targeting cards (Defend, Powers, etc.) have `Target = null`. Always check `TargetType` before using `cardPlay.Target`:
```csharp
protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
{
    // WRONG: This crashes for Self-targeting cards
    await DamageCmd.Attack(8).Targeting(play.Target).Execute(ctx);

    // RIGHT: Check target type
    if (TargetType == TargetType.AnyEnemy)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        await DamageCmd.Attack(8).FromCard(this).Targeting(play.Target).Execute(ctx);
    }
}
```

## Card Keywords
Override `CanonicalKeywords` to add keywords:
```csharp
public override IEnumerable<CardKeyword> CanonicalKeywords => new CardKeyword[]
{
    CardKeyword.Exhaust,
};
```

Available: `Exhaust`, `Ethereal`, `Innate`, `Retain`, `Sly`, `Eternal`, `Unplayable`.

## Card Tags
Override `CanonicalTags` for card classification (used by game logic like "play a Strike"):
```csharp
protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag>
{
    CardTag.Strike,   // Counted as a Strike card
};
```

## Hover Tooltips
Override `ExtraHoverTips` so power/keyword references show tooltips on hover:
```csharp
protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
{
    HoverTipFactory.FromPower<VulnerablePower>(),
    HoverTipFactory.FromPower<StrengthPower>(),
};
```

## Upgrade Logic (OnUpgrade)
Override `OnUpgrade()` to change var values when the card is upgraded at a rest site:
```csharp
protected override void OnUpgrade()
{
    DynamicVars.Damage.UpgradeValueBy(4m);    // +4 damage
    DynamicVars.Block.UpgradeValueBy(3m);     // +3 block
    DynamicVars["WeakPower"].UpgradeValueBy(1m);  // +1 Weak stack
}
```

The upgrade description in localization should reflect the new values. The game calculates upgraded values by applying the upgrade increment to the base.

## Localization (cards.json)
```json
{
  "FLAME_STRIKE.title": "Flame Strike",
  "FLAME_STRIKE.description": "Deal [blue]{Damage}[/blue] damage. Apply [blue]{VulnerablePower}[/blue] [gold]Vulnerable[/gold].",
  "FLAME_STRIKE.upgrade.description": "Deal [blue]{Damage}[/blue] damage. Apply [blue]{VulnerablePower}[/blue] [gold]Vulnerable[/gold]."
}
```

Rich text tags: `[gold]` for keywords, `[blue]` for numeric values, `[red]` for negative effects, `[green]` for positive. DynamicVar names in `{braces}` resolve to current values.

The `.upgrade.description` entry is optional — only needed if the wording changes on upgrade (not just the numbers). If omitted, the base description is used with upgraded values.

## Console Test
```
card FLAME_STRIKE
```
Adds the card to your deck immediately. Use in the developer console (backtick key).

## BaseLib Alternative
With BaseLib, extend `CustomCardModel` instead of `CardModel` for auto-registration, ID prefixing, and custom frame support. See `get_baselib_reference` topic `custom_card`.

## Image Requirements
- `images/card_portraits/{name}.png` — 250x190 card display
- `images/card_portraits/big/{name}.png` — 1000x760 full-size view

Use `generate_art` with `asset_type="card"` to auto-generate both sizes, or `process_art` to process existing artwork.
