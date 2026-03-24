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

    public override IEnumerable<CardModel> StartingDeck => [
        ModelDb.Card<StrikeIronclad>(),
        ModelDb.Card<DefendIronclad>(),
    ];

    public override IReadOnlyList<RelicModel> StartingRelics => [
        ModelDb.Relic<BurningBlood>(),
    ];

    public override CardPoolModel CardPool => ModelDb.CardPool<MyCharCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<MyCharRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<MyCharPotionPool>();
}
```

## Visual Assets
- `CustomVisualPath` — Path to creature_visuals `.tscn` scene
- `CreateCustomVisuals()` — Method to create `NCreatureVisuals` from scene
- `CustomTrailPath` — Trail effect path
- `CustomIconTexturePath` — Character icon texture
- `CustomIconPath` — Character icon path

## Character Select UI
- `CustomCharacterSelectBg` — Select screen background
- `CustomCharacterSelectIconPath` — Character select icon
- `CustomCharacterSelectLockedIconPath` — Locked state icon
- `CustomCharacterSelectTransitionPath` — Transition scene
- `CustomMapMarkerPath` — Map marker icon

## Animation
- `SetupCustomAnimationStates(MegaSprite)` — Override for custom spine animations
- `SetupAnimationState()` — Static helper for standard animation state machine setup

## Audio
- `CustomAttackSfx` — Attack sound effect
- `CustomCastSfx` — Cast/skill sound effect
- `CustomDeathSfx` — Death sound effect

## Energy Counter
```csharp
// Custom energy counter with layer images and colors:
public override CustomEnergyCounter? CustomEnergyCounter => new CustomEnergyCounter(
    layerPathFunc: (index) => $"res://MyMod/images/charui/energy_layer_{index}.png",
    colors: new[] { Color, Color.Darkened(0.3f) }
);
// Or use a scene path:
public override string? CustomEnergyCounterPath => "res://MyMod/scenes/energy_counter.tscn";
```

## Rock-Paper-Scissors (Multiplayer)
- `CustomArmPointingTexturePath`
- `CustomArmRockTexturePath`
- `CustomArmPaperTexturePath`
- `CustomArmScissorsTexturePath`

## Misc Overrides
- `CustomRestSiteAnimPath` — Rest site animation
- `CustomMerchantAnimPath` — Merchant animation
- `StartingGold` — Default 99

## Pool Models (Create Alongside Character)

### CustomCardPoolModel
```csharp
public class MyCharCardPool : CustomCardPoolModel
{
    public override string Title => MyChar.CharacterId;
    public override float H => 1f;   // Hue shift
    public override float S => 1f;   // Saturation
    public override float V => 1f;   // Value/brightness
    public override Color DeckEntryCardColor => new("ff6644");
    public override bool IsColorless => false;
    public override bool IsShared => false;  // true = shared pool (all characters)
}
```

### CustomRelicPoolModel / CustomPotionPoolModel
Similar structure. Set `IsShared = true` to register as a shared pool.

All pool models support `BigEnergyIconPath`, `TextEnergyIconPath`, and `EnergyColorName` for custom energy icons.

## PlaceholderCharacterModel
The NuGet character template starts with `PlaceholderCharacterModel` for staged development, providing sensible defaults while you build out the character.

## Registration
Register content with `[Pool(typeof(MyCharCardPool))]` on cards, relics, and potions.
