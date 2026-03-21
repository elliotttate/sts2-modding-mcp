# BaseLib: Configuration System

## SimpleModConfig
```csharp
public class MyConfig : SimpleModConfig
{
    public override string FileName => "my_config";

    [ConfigSection("General")]
    public bool EnableFeature { get; set; } = true;

    [ConfigSection("Tuning")]
    [SliderRange(0.5, 3.0, 0.1)]
    [SliderLabelFormat("{0:0.00}x")]
    public double DamageMultiplier { get; set; } = 1.0;
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

## Features:
- Auto-generates in-game UI with config button in top bar
- Supports bool (checkbox), double (slider), enum (dropdown)
- `[ConfigSection("Name")]` groups properties under headers
- `[SliderRange(min, max, step)]` for numeric ranges
- Auto-saves after 5s delay when changed
- Saved to `%APPDATA%\.baselib\{ModName}\{FileName}.cfg`
