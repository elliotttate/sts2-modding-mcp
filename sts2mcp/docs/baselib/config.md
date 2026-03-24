# BaseLib: Configuration System

## SimpleModConfig (Recommended)

Auto-generates UI from static properties:

```csharp
public class MyConfig : SimpleModConfig
{
    public override string FileName => "my_config";

    [ConfigSection("General")]
    public bool EnableFeature { get; set; } = true;

    [ConfigSection("Tuning")]
    [SliderRange(0.5, 3.0, 0.1)]
    [SliderLabelFormat("{0:0.00}x")]
    [ConfigHoverTip(true)]
    public double DamageMultiplier { get; set; } = 1.0;

    [ConfigSection("Mode")]
    public MyEnum SelectedMode { get; set; } = MyEnum.Default;
}
```

## Registration (in ModEntry.Init()):
```csharp
var config = new MyConfig();
ModConfigRegistry.Register("mymodid", config);
```

## Access anywhere:
```csharp
var config = ModConfigRegistry.Get<MyConfig>("mymodid");
if (config.EnableFeature) { ... }
```

## Attributes
- `[ConfigSection("Name")]` — groups properties under a section header
- `[SliderRange(min, max, step)]` — for double properties, defines slider range
- `[SliderLabelFormat("{0:0.00}x")]` — format string for slider value display
- `[ConfigHoverTip(enabled)]` — add a hover tooltip to a specific property
- `[HoverTipsByDefault]` — class-level attribute: auto-add hover tips to all properties

## Supported Property Types
- `bool` — rendered as `NConfigTickbox` (checkbox)
- `double` — rendered as `NConfigSlider` (requires `[SliderRange]`)
- `enum` — rendered as `NConfigDropdown`

## Localization
Config UI labels use `settings_ui.json` with keys:
```json
{
    "MYMOD-ENABLE_FEATURE.title": "Enable Feature",
    "MYMOD-DAMAGE_MULTIPLIER.title": "Damage Multiplier",
    "MYMOD-DAMAGE_MULTIPLIER.hover.title": "Damage Multiplier",
    "MYMOD-DAMAGE_MULTIPLIER.hover.desc": "Adjusts damage scaling for all cards."
}
```

## ModConfig (Advanced)
For full control over the config UI, extend `ModConfig` instead and override `SetupConfigUI(Control)` to build the UI yourself.

## Features
- Auto-generates in-game UI with config button in top bar
- Auto-saves after 5s delay when changed
- Saved to `OS.GetUserDataDir()/mod_configs/{FileName}.cfg`
