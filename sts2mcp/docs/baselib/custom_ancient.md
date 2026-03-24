# BaseLib: CustomAncientModel

Create ancient (legendary) events. Inherits from `AncientEventModel`.

## Key Features
- `OptionPools` system — manages 3 option slots with weighted random selection
- Automatic dialogue loading from localization
- Force-spawn control with `ShouldForceSpawn()` / `IsValidForAct()`
- Map/run history icon paths

## Required Overrides
- `MakeOptionPools` property — return `OptionPools` instance defining relic pools for 3 options
- `IsValidForAct(ActModel)` — check if ancient should spawn in a given act

## OptionPools Constructors
```csharp
// Single pool for all 3 options:
protected override OptionPools MakeOptionPools => new(myRelicPool);

// First 2 options share a pool, 3rd uses a different pool:
protected override OptionPools MakeOptionPools => new(commonPool, rarePool);

// Separate pools for each option:
protected override OptionPools MakeOptionPools => new(pool1, pool2, pool3);
```

`Roll(rng)` generates 3 random options (removes selected items from pools).

## AncientOption
```csharp
// Simple relic option (implicit conversion):
AncientOption option = ModelDb.Relic<MyRelic>();

// With preprocessing:
new AncientOption<MyRelic>(relic => relic.SomeSetup());

// With variants (like SeaGlass pattern):
new AncientOption<MyRelic>() { Variants = () => GenerateAllVariants() };
```

## Asset Paths
- `CustomScenePath` — event background scene
- `CustomMapIconPath` / `CustomMapIconOutlinePath` — map icons
- `CustomRunHistoryIcon` / `CustomRunHistoryIconOutline` — run history icons

## Localization Format

### Dialogue Keys
Ancient dialogue is auto-loaded from localization using `AncientDialogueUtil`. Key format:
```
{ancient_id}.talk.{character_id}.{index}-{line}.{type}
```

**Types:**
- `.ancient` — ancient NPC speaks
- `.char` — player character speaks
- `-{line}r` — repeated line (played in sequence)

**Example keys:**
```json
{
    "MY_ANCIENT.talk.ironclad.0-0.ancient": "Welcome, warrior.",
    "MY_ANCIENT.talk.ironclad.0-1.char": "What do you want?",
    "MY_ANCIENT.talk.ironclad.1-0.ancient": "Choose wisely.",
    "MY_ANCIENT.talk.all.0-0.ancient": "Fallback dialogue for any character."
}
```

Use `"all"` as the character_id for fallback dialogue that applies to any character.

### SFX
`SfxPath(dialogueLoc)` extracts the SFX path from a localization entry.

## Spawn Control
- `IsValidForAct(ActModel act)` — return true if this ancient can appear in the act. Use `act.ActNumber()` extension.
- `ShouldForceSpawn(ActModel act, AncientEventModel rngChosen)` — force this ancient to spawn instead of the RNG-chosen one. Use cautiously.

## DefineDialogues
Override `DefineDialogues()` to auto-load dialogues from localization. The base implementation uses `AncientDialogueUtil.GetDialoguesForKey()` to scan your localization table for matching key patterns.
