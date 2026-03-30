# BaseLib: CustomCharacterModel

Full pipeline for creating new playable characters. Inherits from `CharacterModel` and auto-registers via `ModelDbCustomCharacters.Register()`.

## Minimal Example
```csharp
public class MyChar : CustomCharacterModel
{
    public const string CharacterId = "MyChar";
    public static readonly Color Color = new("ff6644");

    public override Color NameColor => Color;
    public override CharacterGender Gender => CharacterGender.Neutral;
    public override int StartingHp => 70;
    public override int StartingGold => 99;

    public override IEnumerable<CardModel> StartingDeck => [
        ModelDb.Card<StrikeMyChar>(),
        ModelDb.Card<StrikeMyChar>(),
        ModelDb.Card<StrikeMyChar>(),
        ModelDb.Card<StrikeMyChar>(),
        ModelDb.Card<DefendMyChar>(),
        ModelDb.Card<DefendMyChar>(),
        ModelDb.Card<DefendMyChar>(),
        ModelDb.Card<DefendMyChar>(),
        ModelDb.Card<SpecialStarter1>(),
        ModelDb.Card<SpecialStarter2>(),
    ];

    public override IReadOnlyList<RelicModel> StartingRelics => [
        ModelDb.Relic<MyStarterRelic>(),
    ];

    public override CardPoolModel CardPool => ModelDb.CardPool<MyCharCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<MyCharRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<MyCharPotionPool>();
}
```

## Required Overrides

| Property | Type | Description |
|----------|------|-------------|
| `NameColor` | `Color` | Character name color in UI |
| `Gender` | `CharacterGender` | `Neutral`, `Masculine`, or `Feminine` — affects combat pronouns |
| `StartingHp` | `int` | Starting maximum HP |
| `StartingDeck` | `IEnumerable<CardModel>` | Starting deck (typically 10 cards) |
| `StartingRelics` | `IReadOnlyList<RelicModel>` | Starting relics (typically 1) |
| `CardPool` | `CardPoolModel` | Card pool via `ModelDb.CardPool<T>()` |
| `RelicPool` | `RelicPoolModel` | Relic pool via `ModelDb.RelicPool<T>()` |
| `PotionPool` | `PotionPoolModel` | Potion pool via `ModelDb.PotionPool<T>()` |

## Visual Paths

### Combat & Character Display
- `CustomVisualPath` — Path to creature_visuals `.tscn` scene (NCreatureVisuals)
- `CreateCustomVisuals()` — Alternative: method to create `NCreatureVisuals` programmatically
- `CustomTrailPath` — Card play trail particle effect `.tscn`
- `CustomIconPath` — Character icon `.tscn` scene
- `CustomIconTexturePath` — Character icon `.png` texture (top panel, ~64x64)
- `CustomRestSiteAnimPath` — Rest site character display `.tscn`
- `CustomMerchantAnimPath` — Merchant character display `.tscn`

### Character Select UI
- `CustomCharacterSelectBg` — Select screen background `.tscn`
- `CustomCharacterSelectIconPath` — Character select portrait `.png`
- `CustomCharacterSelectLockedIconPath` — Locked state portrait `.png`
- `CustomCharacterSelectTransitionPath` — Transition material `.tres`
- `CustomMapMarkerPath` — Map marker icon `.png`
- `CharacterSelectSfx` — Sound on character select `.ogg`

### Multiplayer (Rock-Paper-Scissors)
- `CustomArmPointingTexturePath` — Pointing hand gesture `.png`
- `CustomArmRockTexturePath` — Rock gesture `.png`
- `CustomArmPaperTexturePath` — Paper gesture `.png`
- `CustomArmScissorsTexturePath` — Scissors gesture `.png`

## Color & Theming

```csharp
public static readonly Color Color = new(0.5f, 0.0f, 0.5f);  // Purple
public override Color NameColor => Color;           // Character name in UI
public override Color MapDrawingColor => Color;     // Map path drawing color
```

## Animation

```csharp
// Override for custom Spine/MegaSprite animations:
public override CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
{
    return SetupAnimationState(controller, "Idle", hitName: "Hit");
}

// Animation timing:
public override float AttackAnimDelay => 0.15f;  // Seconds before attack VFX
public override float CastAnimDelay => 0.25f;    // Seconds before cast VFX
```

`SetupAnimationState()` is a static helper that creates a standard state machine with idle and hit states.

## Audio
- `CustomAttackSfx` — Attack sound effect path
- `CustomCastSfx` — Cast/skill sound effect path
- `CustomDeathSfx` — Death sound effect path

## Energy Counter

### Option A: Programmatic (recommended)
```csharp
public override CustomEnergyCounter? CustomEnergyCounter => new CustomEnergyCounter(
    layerPathFunc: i => $"res://MyMod/images/energy/orb_layer_{i}.png",
    Color,                    // Primary glow color
    Color.Lightened(0.3f)     // Secondary glow color
);
```

The `CustomEnergyCounter` constructor:
- `Func<int, string> layerPathFunc` — Returns PNG path for layer index (0-4, 5 layers)
- `Color color1` — Primary energy orb glow color
- `Color color2` — Secondary energy orb glow color

### Option B: Scene-based
```csharp
public override string? CustomEnergyCounterPath => "res://MyMod/scenes/energy_counter.tscn";
```

## Extra Asset Preloading

Preload VFX textures, stance auras, or other assets at game startup:

```csharp
protected override IEnumerable<string> ExtraAssetPaths => [
    "res://MyMod/vfx/aura.tscn",
    "res://MyMod/images/vfx/particle.png",
    .. ModelDb.Power<MyPower>().AssetPaths,  // Spread operator for power assets
];
```

## Architect Attack VFX

```csharp
public override List<string> GetArchitectAttackVfx() => [
    "vfx/vfx_attack_blunt", "vfx/vfx_heavy_blunt", "vfx/vfx_attack_slash",
    "vfx/vfx_bloody_impact", "vfx/vfx_rock_shatter"
];
```

## Misc Overrides
- `StartingGold` — Default 99
- `UnlocksAfterRunAs` — Character required to unlock this one (null = always unlocked)

## Pool Models (Create Alongside Character)

### CustomCardPoolModel
```csharp
public sealed class MyCharCardPool : CustomCardPoolModel
{
    public override string Title => MyChar.CharacterId;
    public override float H => 0.08f;  // Hue (0-1): 0=red, 0.33=green, 0.66=blue, 0.75=purple
    public override float S => 1f;     // Saturation (0-1)
    public override float V => 1f;     // Value/brightness (0-1)
    public override Color DeckEntryCardColor => MyChar.Color;
    public override bool IsColorless => false;
    public override bool IsShared => false;  // true = shared pool (all characters)

    // Custom energy icons on card cost display
    public override string? BigEnergyIconPath => "res://MyMod/images/ui/energy_icon.png";
    public override string? TextEnergyIconPath => "res://MyMod/images/ui/text_energy_icon.png";
}
```

### CustomRelicPoolModel / CustomPotionPoolModel
```csharp
public class MyCharRelicPool : CustomRelicPoolModel
{
    public override string EnergyColorName => MyChar.CharacterId;
    public override Color LabOutlineColor => MyChar.Color;
}
```

All pool models support `BigEnergyIconPath`, `TextEnergyIconPath`, and `EnergyColorName` for custom energy icons. Set `IsShared = true` to register as a shared pool accessible to all characters.

## PlaceholderCharacterModel
The NuGet character template starts with `PlaceholderCharacterModel` for staged development, providing sensible defaults while you build out the character.

## Registration
Register content with `[Pool(typeof(MyCharCardPool))]` on cards, relics, and potions.
