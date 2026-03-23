# SmartFormat in Slay the Spire 2

STS2 uses [SmartFormat.NET](https://github.com/axuno/SmartFormat) (by axuno) for all dynamic text rendering — card descriptions, power tooltips, relic text, and more. This guide covers the full system: the base SmartFormat library features, MegaCrit's custom formatters, the DynamicVar pipeline, and practical examples for modders.

---

## Table of Contents

1. [How It Works (Architecture)](#1-how-it-works-architecture)
2. [Basic SmartFormat Syntax](#2-basic-smartformat-syntax)
3. [Built-in SmartFormat Formatters](#3-built-in-smartformat-formatters)
4. [MegaCrit's Custom Formatters](#4-megacrits-custom-formatters)
5. [The DynamicVar System](#5-the-dynamicvar-system)
6. [Card Description Pipeline](#6-card-description-pipeline)
7. [Power Description Pipeline](#7-power-description-pipeline)
8. [Relic Description Pipeline](#8-relic-description-pipeline)
9. [BBCode Rich Text Tags](#9-bbcode-rich-text-tags)
10. [Practical Examples for Modders](#10-practical-examples-for-modders)
11. [Common Patterns and Recipes](#11-common-patterns-and-recipes)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. How It Works (Architecture)

### The Rendering Pipeline

```
Localization JSON file          C# Code (Card/Power/Relic)
   ┌──────────────────┐         ┌──────────────────────────┐
   │ "MY_CARD.desc":  │         │ CanonicalVars:           │
   │ "Deal {Damage}   │         │   new DamageVar(8m, ...) │
   │  damage"         │         │   new BlockVar(5m, ...)  │
   └────────┬─────────┘         └────────────┬─────────────┘
            │                                │
            ▼                                ▼
      LocString(table, key)      DynamicVars.AddTo(locString)
            │                                │
            └──────────┬─────────────────────┘
                       ▼
              LocManager.SmartFormat()
                       │
                       ▼
              SmartFormatter.Format(
                  cultureInfo,
                  rawTemplate,    ← "Deal {Damage} damage"
                  variables       ← { "Damage": DamageVar(8) }
              )
                       │
                       ▼
                 "Deal 8 damage"
```

### Initialization

`LocManager.LoadLocFormatters()` creates the SmartFormatter instance and registers all extensions:

**Source extensions** (resolve variable names to values):
- `ListFormatter`, `DictionarySource`, `ValueTupleSource`, `ReflectionSource`, `DefaultSource`

**Output formatters** (transform values into text):
- Stock: `PluralLocalizationFormatter`, `ConditionalFormatter`, `ChooseFormatter`, `SubStringFormatter`, `IsMatchFormatter`, `DefaultFormatter`
- Custom: `AbsoluteValueFormatter`, `EnergyIconsFormatter`, `StarIconsFormatter`, `HighlightDifferencesFormatter`, `HighlightDifferencesInverseFormatter`, `PercentMoreFormatter`, `PercentLessFormatter`, `ShowIfUpgradedFormatter`

---

## 2. Basic SmartFormat Syntax

SmartFormat uses curly braces `{}` for placeholders. The general syntax is:

```
{variableName}                    → Simple substitution
{variableName:formatterName}      → Apply a named formatter
{variableName:format(options)}    → Formatter with options
```

### Simple Variable Substitution

```json
"Deal {Damage} damage."           → "Deal 8 damage."
"Gain {Block} block."             → "Gain 5 block."
"Draw {Cards} cards."             → "Draw 2 cards."
```

### Nested Properties

SmartFormat can traverse object properties:

```json
"{owner.Name} gains {Amount} strength."
```

### Escaping Braces

Use `\{` and `\}` to output literal braces in the template, or double them in C# format strings.

---

## 3. Built-in SmartFormat Formatters

These are stock [SmartFormat.NET](https://github.com/axuno/SmartFormat/wiki) features available in STS2:

### Pluralization — `plural`

Selects text based on numeric value. Options are separated by `|`.

```json
"Draw {Cards} {Cards:plural:card|cards}."
```

| Cards value | Output |
|---|---|
| 1 | "Draw 1 card." |
| 3 | "Draw 3 cards." |

For languages with more plural forms (e.g., Russian), SmartFormat automatically uses the locale's plural rules via `PluralLocalizationFormatter`.

### Conditional — `cond` / `conditional`

Selects text based on a numeric comparison to zero:

```json
"{value:cond:negative text|zero text|positive text}"
```

| value | Output |
|---|---|
| -2 | "negative text" |
| 0 | "zero text" |
| 5 | "positive text" |

For booleans:

```json
"{InCombat:cond:Out of combat|In combat}"
```

| InCombat | Output |
|---|---|
| false (0) | "Out of combat" |
| true (1) | "In combat" |

### Choose — `choose`

Selects from a list based on the value itself:

```json
"{index:choose(0|1|2):Attack|Skill|Power}"
```

### Substring — `substr`

Extracts a portion of text:

```json
"{text:substr(0,5)}"  → First 5 characters
```

### IsMatch — `ismatch`

Tests a value against a regex pattern:

```json
"{name:ismatch(^A):starts with A|doesn't start with A}"
```

### List Formatting

When a variable is an `IList<string>`, SmartFormat auto-joins it:

```json
"{items:list:{}|, |, and }"
```

Produces: "Sword, Shield, and Potion"

---

## 4. MegaCrit's Custom Formatters

These 8 formatters are unique to STS2, found in `MegaCrit.Sts2.Core.Localization.Formatters`:

### `:abs` — Absolute Value

**Formatter name:** `abs`
**Accepts:** decimal, double, float, int, long, short

Returns the absolute value of a number. Useful when you want to display a magnitude without sign.

```json
"Lose {HpLoss:abs} HP."
```

| HpLoss value | Output |
|---|---|
| -3 | "Lose 3 HP." |
| 5 | "Lose 5 HP." |

### `:energyIcons` — Energy Icon Sprites

**Formatter name:** `energyIcons`
**Accepts:** `EnergyVar`, `CalculatedVar`, decimal, int, string

Renders energy values as inline Godot BBCode image sprites. The icon color matches the current character's energy color (e.g., red for Ironclad).

```json
"Costs {Energy:energyIcons}."
```

**Rendering rules:**
- **1–3 energy:** Repeats the icon sprite that many times (e.g., `[img]..._energy_icon.png[/img]` x2 for 2 energy)
- **0 or 4+ energy:** Shows the number followed by a single icon (e.g., `5[img]..._energy_icon.png[/img]`)

The icon path is: `res://images/packed/sprite_fonts/{colorPrefix}_energy_icon.png`

The `colorPrefix` is determined by the character and is automatically set via `EnergyIconHelper.GetPrefix()` during description rendering.

### `:starIcons` — Star Icon Sprites

**Formatter name:** `starIcons`
**Accepts:** `DynamicVar`, decimal, int

Renders a number of star icon sprites. Used for star costs.

```json
"Costs {Stars:starIcons}."
```

Always repeats the star icon for the count: `[img]res://images/packed/sprite_fonts/star_icon.png[/img]` repeated N times.

### `:diff` — Highlight Differences

**Formatter name:** `diff`
**Accepts:** `DynamicVar` only

Compares `PreviewValue` against `EnchantedValue` and wraps the number in color BBCode:

- **PreviewValue > EnchantedValue** → `[green]value[/green]` (buffed)
- **PreviewValue < EnchantedValue** → `[red]value[/red]` (debuffed)
- **PreviewValue == EnchantedValue** → plain value (no color)
- **WasJustUpgraded == true** → always `[green]` (upgrade highlight)

```json
"Deal {Damage:diff} damage."
```

In combat with +2 Strength:
- Base damage 8, preview 10 → `"Deal [green]10[/green] damage."`

This is how the game shows real-time damage/block previews that change color based on buffs and debuffs.

### `:inverseDiff` — Inverse Highlight Differences

**Formatter name:** `inverseDiff`
**Accepts:** `DynamicVar` only

Same as `:diff` but inverts the comparison — used for values where *lower is better*. If `EnchantedValue > PreviewValue`, it shows green instead of red.

```json
"Enemy takes {Damage:inverseDiff} damage for {ReductionAmount:inverseDiff} turns."
```

### `:percentMore` — Percent Increase

**Formatter name:** `percentMore`
**Accepts:** `DynamicVar`, or any `IConvertible` decimal

Converts a multiplier to a percentage increase: `(value - 1) × 100`

```json
"Deal {DamageMultiplier:percentMore}% more damage."
```

| DamageMultiplier | Output |
|---|---|
| 1.5 | "Deal 50% more damage." |
| 2.0 | "Deal 100% more damage." |
| 1.25 | "Deal 25% more damage." |

### `:percentLess` — Percent Decrease

**Formatter name:** `percentLess`
**Accepts:** `DynamicVar`, or any `IConvertible` decimal

Converts a multiplier to a percentage decrease: `(1 - value) × 100`

```json
"Take {DamageReduction:percentLess}% less damage."
```

| DamageReduction | Output |
|---|---|
| 0.75 | "Take 25% less damage." |
| 0.5 | "Take 50% less damage." |

### `:show` — Show If Upgraded

**Formatter name:** `show`
**Accepts:** `IfUpgradedVar` only

Conditional formatting based on the card's upgrade state. Format uses `|` to separate options:

```
{IfUpgraded:show(upgraded text|normal text)}
```

- **1st option** = Text to show when **upgraded** or in **upgrade preview**
- **2nd option** (optional) = Text to show when **not upgraded**

**Behavior by UpgradeDisplay state:**

| State | Behavior |
|---|---|
| `Normal` (not upgraded) | Shows the 2nd option (normal text) |
| `Upgraded` | Shows the 1st option (upgraded text) |
| `UpgradePreview` | Shows the 1st option wrapped in `[green]...[/green]` |

This enables a single localization entry to handle both base and upgraded card descriptions:

```json
"Deal {Damage:diff} damage.{IfUpgraded:show(\nDraw 1 card.|)}"
```

- **Base card:** "Deal 8 damage." (the `|)` means empty string for normal)
- **Upgraded card:** "Deal 8 damage.\nDraw 1 card."
- **Upgrade preview:** "Deal 8 damage.\n[green]Draw 1 card.[/green]"

---

## 5. The DynamicVar System

DynamicVars are the bridge between game state and localization templates. Every card, power, and relic declares its variables via `CanonicalVars`, which get added to the `LocString` before formatting.

### DynamicVar Base Class

```csharp
public class DynamicVar : IConvertible
{
    public string Name { get; }          // Placeholder name in templates
    public decimal BaseValue { get; }    // Immutable default
    public decimal EnchantedValue { get; } // After enchantments
    public decimal PreviewValue { get; }   // In-combat preview (with buffs/debuffs)
    public bool WasJustUpgraded { get; }   // Set during upgrade animation
}
```

SmartFormat uses `PreviewValue` when rendering (via `IConvertible.ToDecimal()`), which reflects the current combat state.

### DynamicVar Types

| Type | Default Name | Constructor | Notes |
|---|---|---|---|
| `DamageVar` | `"Damage"` | `DamageVar(decimal, ValueProp)` | Runs Hook.ModifyDamage during preview |
| `BlockVar` | `"Block"` | `BlockVar(decimal, ValueProp)` | Runs Hook.ModifyBlock during preview |
| `EnergyVar` | `"Energy"` | `EnergyVar(string name, int)` | Has `ColorPrefix` for icon color |
| `CardsVar` | `"Cards"` | `CardsVar(int)` or `CardsVar(string, int)` | Card draw/discard amounts |
| `PowerVar<T>` | `typeof(T).Name` | `PowerVar<T>(decimal)` | Auto-names from power class |
| `HealVar` | `"Heal"` | `HealVar(string, decimal)` | Healing amounts |
| `HpLossVar` | `"HpLoss"` | `HpLossVar(string, decimal)` | HP loss amounts |
| `MaxHpVar` | `"MaxHp"` | `MaxHpVar(string, decimal)` | Max HP changes |
| `GoldVar` | `"Gold"` | `GoldVar(string, int)` | Currency amounts |
| `StarsVar` | `"Stars"` | `StarsVar(string, int)` | Star costs |
| `ForgeVar` | `"Forge"` | `ForgeVar(string, int)` | Forge amounts |
| `RepeatVar` | `"Repeat"` | `RepeatVar(string, int)` | Number of times to repeat |
| `SummonVar` | `"Summon"` | `SummonVar(string, decimal)` | Summon count, runs Hook.ModifySummonAmount |
| `IntVar` | custom | `IntVar(string, int)` | Generic integer |
| `StringVar` | custom | `StringVar(string)` | Text value |
| `BoolVar` | custom | `BoolVar(string, bool)` | Boolean |
| `ExtraDamageVar` | `"ExtraDamage"` | `ExtraDamageVar(decimal)` | Bonus damage modifier |
| `OstyDamageVar` | `"OstyDamage"` | `OstyDamageVar(decimal, ValueProp)` | Damage from summoned creature |
| `IfUpgradedVar` | `"IfUpgraded"` | `IfUpgradedVar(UpgradeDisplay)` | Upgrade state for `:show` |

### Calculated Variables

For values that depend on game state (e.g., "damage equal to your block"), STS2 uses a three-variable pattern:

| Type | Name | Purpose |
|---|---|---|
| `CalculationBaseVar` | `"CalculationBase"` | Additive base (often 0) |
| `CalculationExtraVar` | `"CalculationExtra"` | Multiplier coefficient |
| `CalculatedDamageVar` | `"CalculatedDamage"` | Result: `Base + (Extra × multiplier_func)` |
| `CalculatedBlockVar` | `"CalculatedBlock"` | Same formula for block |

**Formula:** `CalculatedDamage = CalculationBase + (CalculationExtra × multiplierCalc(card, target))`

**Example — Body Slam** (deal damage equal to current block):
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new CalculationBaseVar(0m),
    new ExtraDamageVar(1m),
    new CalculatedDamageVar(ValueProp.Move)
        .WithMultiplier((card, _) => card.Owner.Creature.Block)
};
// Result: 0 + (1 × currentBlock) = currentBlock damage
```

**Example — Ashen Strike** (6 base + 3 per exhausted card):
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new CalculationBaseVar(6m),
    new ExtraDamageVar(3m),
    new CalculatedDamageVar(ValueProp.Move)
        .WithMultiplier((card, _) => PileType.Exhaust.GetPile(card.Owner).Cards.Count)
};
// Result: 6 + (3 × exhaustPileCount)
```

### PowerVar Auto-Naming

`PowerVar<T>` derives its name from the type parameter:

```csharp
new PowerVar<StrengthPower>(2m)      // Name = "StrengthPower"
new PowerVar<VulnerablePower>(3m)    // Name = "VulnerablePower"
new PowerVar<WeakPower>(1m)          // Name = "WeakPower"
new PowerVar<DexterityPower>(1m)     // Name = "DexterityPower"
new PowerVar<ThornsPower>(4m)        // Name = "ThornsPower"
new PowerVar<PoisonPower>(5m)        // Name = "PoisonPower"
```

You can also override the name:
```csharp
new PowerVar<WeakPower>("SappingWeak", 2m)  // Name = "SappingWeak"
```

### DynamicVarSet

`DynamicVarSet` is the container that holds all of an entity's DynamicVars. It:
- Prevents duplicate names (throws if two vars share a name)
- Provides typed property accessors (`.Damage`, `.Block`, `.Energy`, etc.)
- Adds all vars to a LocString via `AddTo(locString)`
- Handles cloning, preview reset, and upgrade finalization

---

## 6. Card Description Pipeline

When a card renders its description, this is the full sequence:

```csharp
// CardModel.GetDescriptionForPile()
private string GetDescriptionForPile(PileType pileType, DescriptionPreviewType previewType, Creature? target)
{
    // 1. Load the raw template from localization
    LocString description = Description;  // → LocString("cards", "{MODEL_ID}.description")

    // 2. Add all CanonicalVars (Damage, Block, etc.)
    DynamicVars.AddTo(description);

    // 3. Add extra custom variables (override point for subclasses)
    AddExtraArgsToDescription(description);

    // 4. Add standard variables available to ALL cards
    description.Add(new IfUpgradedVar(upgradeDisplay));
    description.Add("OnTable", isOnTable);       // bool: is card in hand/play area?
    description.Add("InCombat", inCombat);        // bool: is a combat in progress?
    description.Add("IsTargeting", hasTarget);    // bool: is the player targeting?
    description.Add("energyPrefix", prefix);      // string: character energy color
    description.Add("singleStarIcon", "...");     // string: star icon BBCode

    // 5. Set energy icon colors
    foreach (var energyVar in description.Variables.OfType<EnergyVar>())
        energyVar.ColorPrefix = prefix;

    // 6. Format the template with SmartFormat
    string result = description.GetFormattedText();

    // 7. Append enchantment/affliction text (wrapped in [purple])
    // 8. Prepend/append keyword text (Retain, Sly, etc.)
    // 9. Join with newlines
    return string.Join('\n', parts);
}
```

### Standard Variables Available in All Card Descriptions

| Variable | Type | Description |
|---|---|---|
| `{IfUpgraded}` | `IfUpgradedVar` | Upgrade state for `:show` formatter |
| `{OnTable}` | bool | Card is in hand or play area |
| `{InCombat}` | bool | A combat is currently in progress |
| `{IsTargeting}` | bool | Player is targeting with this card |
| `{energyPrefix}` | string | Character's energy icon color prefix |
| `{singleStarIcon}` | string | BBCode for one star icon |

### Localization Keys for Cards

In your `localization/eng/cards.json`:

```json
{
    "MY_CARD.title": "My Card",
    "MY_CARD.description": "Deal {Damage:diff} damage.\nGain {Block:diff} [gold]Block[/gold].",
    "MY_CARD.upgrade.description": "Deal {Damage:diff} damage.\nGain {Block:diff} [gold]Block[/gold].\nDraw 1 card."
}
```

Or using `:show` for a single entry:

```json
{
    "MY_CARD.title": "My Card",
    "MY_CARD.description": "Deal {Damage:diff} damage.\nGain {Block:diff} [gold]Block[/gold].{IfUpgraded:show(\nDraw 1 card.|)}"
}
```

---

## 7. Power Description Pipeline

Powers use a `smartDescription` key pattern:

```csharp
// PowerModel.HoverTips getter
LocString locString = SmartDescription;  // → LocString("powers", "{ID}.smartDescription")

// Add standard power variables
locString.Add("Amount", Amount);          // decimal: current stack count
locString.Add("OnPlayer", Owner.IsPlayer);
locString.Add("IsMultiplayer", playerCount > 1);
locString.Add("PlayerCount", playerCount);
locString.Add("OwnerName", ownerTitle);
locString.Add("ApplierName", applierTitle);
locString.Add("TargetName", targetTitle);

// Add custom + canonical vars
AddDumbVariablesToDescription(locString);
DynamicVars.AddTo(locString);

string text = locString.GetFormattedText();
```

### Localization Keys for Powers

```json
{
    "STRENGTH.title": "Strength",
    "STRENGTH.description": "Increases attack damage by {Amount}.",
    "STRENGTH.smartDescription": "Increases attack damage by {Amount}."
}
```

The game checks `HasSmartDescription` (whether `{ID}.smartDescription` exists in the loc table). If it does, that key is used with dynamic variables. If not, the plain `.description` key is used as a static fallback.

### Standard Variables Available in Power Descriptions

| Variable | Type | Description |
|---|---|---|
| `{Amount}` | decimal | Current stack count of the power |
| `{OnPlayer}` | bool | Power is on a player (vs monster) |
| `{IsMultiplayer}` | bool | More than 1 player in combat |
| `{PlayerCount}` | int | Number of players |
| `{OwnerName}` | string | Title of the creature that has the power |
| `{ApplierName}` | string | Title of who applied the power |
| `{TargetName}` | string | Title of the power's target |

---

## 8. Relic Description Pipeline

```csharp
// RelicModel.DynamicDescription getter
LocString description = Description;  // → LocString("relics", "{ID}.description")
DynamicVars.AddTo(description);
description.Add("energyPrefix", prefix);
description.Add("singleStarIcon", "...");

// Set energy icon colors
foreach (var energyVar in description.Variables.OfType<EnergyVar>())
    energyVar.ColorPrefix = prefix;
```

### Localization Keys for Relics

```json
{
    "MY_RELIC.title": "My Relic",
    "MY_RELIC.description": "At the start of each combat, gain {VigorPower} [gold]Vigor[/gold].",
    "MY_RELIC.flavor": "A finely polished stone that pulses with energy."
}
```

---

## 9. BBCode Rich Text Tags

STS2 uses Godot's BBCode for rich text. These can be freely mixed with SmartFormat placeholders:

| Tag | Usage | Example |
|---|---|---|
| `[gold]...[/gold]` | Keyword highlighting | `[gold]Block[/gold]` |
| `[blue]...[/blue]` | Dynamic numbers | `[blue]{Damage}[/blue]` |
| `[red]...[/red]` | Negative/debuff text | `[red]Lose 3 HP[/red]` |
| `[green]...[/green]` | Positive/buff text | `[green]+2 Strength[/green]` |
| `[gray]...[/gray]` | Subdued text | `[gray](unplayable)[/gray]` |
| `[purple]...[/purple]` | Enchantment text | Added automatically |
| `[img]path[/img]` | Inline image sprite | Energy/star icons |

Note: The `:diff` formatter automatically adds `[green]`/`[red]` wrapping, so you typically don't need to manually color dynamic values.

---

## 10. Practical Examples for Modders

### Example 1: Simple Attack Card

**C# — Card Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new DamageVar(8m, ValueProp.Move)
};

protected override void OnUpgrade()
{
    DynamicVars.Damage.UpgradeValueBy(3m);  // 8 → 11
}
```

**JSON — Localization:**
```json
{
    "MY_ATTACK.title": "My Attack",
    "MY_ATTACK.description": "Deal {Damage:diff} damage."
}
```

**Output:** "Deal 8 damage." (base), "Deal [green]11[/green] damage." (upgrade preview)

### Example 2: Attack + Block Card with Upgrade-Only Text

**C# — Card Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new DamageVar(6m, ValueProp.Move),
    new BlockVar(4m, ValueProp.Move),
    new CardsVar(1)
};

protected override void OnUpgrade()
{
    DynamicVars.Damage.UpgradeValueBy(2m);
    DynamicVars.Block.UpgradeValueBy(2m);
}
```

**JSON — Localization:**
```json
{
    "BALANCED_STRIKE.title": "Balanced Strike",
    "BALANCED_STRIKE.description": "Deal {Damage:diff} damage.\nGain {Block:diff} [gold]Block[/gold].{IfUpgraded:show(\nDraw {Cards} {Cards:plural:card|cards}.|)}"
}
```

**Output (base):** "Deal 6 damage.\nGain 4 Block."
**Output (upgraded):** "Deal 8 damage.\nGain 6 Block.\nDraw 1 card."
**Output (upgrade preview):** "Deal [green]8[/green] damage.\nGain [green]6[/green] Block.\n[green]Draw 1 card.[/green]"

### Example 3: Apply a Debuff

**C# — Card Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new DamageVar(8m, ValueProp.Move),
    new PowerVar<VulnerablePower>(2m)
};
```

**JSON — Localization:**
```json
{
    "BASH.title": "Bash",
    "BASH.description": "Deal {Damage:diff} damage.\nApply {VulnerablePower:diff} [gold]Vulnerable[/gold]."
}
```

### Example 4: Energy Cost in Description

**C# — Card Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new DamageVar(12m, ValueProp.Move),
    new EnergyVar("EnergyCost", 1)
};
```

**JSON — Localization:**
```json
{
    "ENERGY_BLAST.title": "Energy Blast",
    "ENERGY_BLAST.description": "Deal {Damage:diff} damage.\nSpend {EnergyCost:energyIcons} to repeat."
}
```

### Example 5: Scaling Damage (Calculated)

**C# — Card Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new CalculationBaseVar(4m),
    new ExtraDamageVar(2m),
    new CalculatedDamageVar(ValueProp.Move)
        .WithMultiplier((card, _) => PileType.Exhaust.GetPile(card.Owner).Cards.Count)
};
```

**JSON — Localization:**
```json
{
    "PYRE_STRIKE.title": "Pyre Strike",
    "PYRE_STRIKE.description": "Deal {CalculatedDamage:diff} damage.\n(base {CalculationBase} + {CalculationExtra} per exhausted card)"
}
```

### Example 6: Percent-Based Multiplier

**C# — Card Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new DynamicVar("DamageMultiplier", 1.5m)
};
```

**JSON — Localization:**
```json
{
    "EMPOWER.title": "Empower",
    "EMPOWER.description": "Your next attack deals {DamageMultiplier:percentMore}% more damage."
}
```

**Output:** "Your next attack deals 50% more damage."

### Example 7: Relic with Power Variable

**C# — Relic Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars =>
    new DynamicVar[] { new PowerVar<VigorPower>(8m) };
```

**JSON — Localization:**
```json
{
    "AKABEKO.title": "Akabeko",
    "AKABEKO.description": "At the start of each combat, gain {VigorPower} [gold]Vigor[/gold]."
}
```

### Example 8: Power with SmartDescription

**C# — Power Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars =>
    new DynamicVar[] { new DynamicVar("DamageDecrease", 0.5m) };
```

**JSON — Localization:**
```json
{
    "COLOSSUS.title": "Colossus",
    "COLOSSUS.description": "Take reduced damage.",
    "COLOSSUS.smartDescription": "Take {DamageDecrease:percentLess}% less damage. ({Amount} {Amount:plural:stack|stacks})"
}
```

**Output (3 stacks):** "Take 50% less damage. (3 stacks)"

### Example 9: Conditional Text Based on Game State

**JSON — Localization:**
```json
{
    "CONTEXT_CARD.description": "{InCombat:cond:Deal {Damage:diff} damage.|Deal {Damage} damage.}"
}
```

- **In combat:** Uses `:diff` coloring (preview-aware)
- **Out of combat (deck view):** Shows plain number

### Example 10: Custom Named Variables with AddExtraArgsToDescription

**C# — Card Model:**
```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
{
    new DamageVar(12m, ValueProp.Move),
    new BlockVar(8m, ValueProp.Move),
    new EnergyVar("CostEnergy", 2),
    new CardsVar("WisdomCards", 3)
};

protected override void AddExtraArgsToDescription(LocString description)
{
    description.Add("CardType", _currentType.ToString());
    description.Add("HasRider", _riderActive);
}
```

**JSON — Localization:**
```json
{
    "COMPLEX_CARD.description": "{HasRider:cond:|No rider effect.\n}Deal {Damage:diff} damage.\nGain {Block:diff} [gold]Block[/gold].\nDraw {WisdomCards} {WisdomCards:plural:card|cards}.\nCosts {CostEnergy:energyIcons}."
}
```

---

## 11. Common Patterns and Recipes

### Pattern: Upgrade Increases a Number

```csharp
// In OnUpgrade():
DynamicVars["Damage"].UpgradeValueBy(3m);   // DamageVar: 8 → 11
DynamicVars["Block"].UpgradeValueBy(2m);     // BlockVar: 5 → 7
DynamicVars["WeakPower"].UpgradeValueBy(1m); // PowerVar: 2 → 3
```

The `:diff` formatter automatically shows the upgrade in green during preview.

### Pattern: Upgrade Adds New Text

Use `:show` with the `IfUpgraded` variable (automatically added to all cards):

```json
"Deal {Damage:diff} damage.{IfUpgraded:show(\n[gold]Exhaust[/gold].|)}"
```

### Pattern: Duplicate Variable Names

If you need two of the same type (e.g., two separate block values), you **must** provide custom names:

```csharp
// WRONG — throws ArgumentException for duplicate key "Block":
new DynamicVar[] {
    new BlockVar(3m, ValueProp.Move),
    new BlockVar(5m, ValueProp.Move)  // ERROR!
};

// CORRECT — use custom names:
new DynamicVar[] {
    new BlockVar("InitialBlock", 3m, ValueProp.Move),
    new BlockVar("BonusBlock", 5m, ValueProp.Move)
};
```

```json
"Gain {InitialBlock:diff} [gold]Block[/gold].\nGain {BonusBlock:diff} additional [gold]Block[/gold] if combo."
```

### Pattern: Variable Name Spaces

Spaces in variable names are automatically converted to hyphens in `LocString.AddObj()`:

```csharp
description.Add("Extra Damage", 5m);  // Stored as "Extra-Damage"
```

```json
"{Extra-Damage} bonus damage"
```

### Pattern: Nested LocString Variables

When you add a `LocString` as a variable, it's resolved to formatted text first:

```csharp
description.Add("description", DynamicDescription);  // Calls GetFormattedText() internally
```

---

## 12. Troubleshooting

### FormattingException / ParsingErrors

`LocManager.SmartFormat()` catches these and:
1. Logs the error with table, key, and variable dump
2. Reports to Sentry
3. Returns the **raw unformatted text** as a fallback

Common causes:
- Missing variable: `{Damage}` in template but no `DamageVar` in `CanonicalVars`
- Mismatched braces: `{Damage` without closing `}`
- Wrong formatter for type: `:diff` only works with `DynamicVar`, not raw numbers

### Formatter Not Triggering

Each formatter checks the type of `formattingInfo.CurrentValue`:
- `:diff` / `:inverseDiff` — requires `DynamicVar` (returns false otherwise)
- `:show` — requires `IfUpgradedVar`
- `:energyIcons` — requires `EnergyVar`, `CalculatedVar`, decimal, int, or string
- `:abs` — requires a numeric type

If the type doesn't match, the formatter returns `false` and SmartFormat falls through to the next registered formatter.

### Variable Not Found

If a variable referenced in the template doesn't exist in the dictionary, SmartFormat throws a `FormattingException`. Make sure every `{placeholder}` in your localization JSON has a corresponding variable added via `CanonicalVars` or `AddExtraArgsToDescription`.

### CultureInfo and Number Formatting

The formatter uses the current locale's `CultureInfo` for number formatting (decimal separators, etc.), except for keys marked as "local" in the loc table, which use `EnglishCultureInfo`. The `:percentMore` and `:percentLess` formatters always use `CultureInfo.InvariantCulture`.
