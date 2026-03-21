# ModConfig Integration

## Option 1: BaseLib SimpleModConfig (Recommended)
If you already depend on BaseLib:
```csharp
public class MyConfig : SimpleModConfig
{
    public override string ModId => "mymod";

    [ConfigEntry("Enable Feature", section: "General")]
    public bool EnableFeature { get; set; } = true;

    [ConfigEntry("Damage Multiplier", section: "Balance")]
    [Slider(min: 0.5, max: 3.0, step: 0.1)]
    public double DamageMultiplier { get; set; } = 1.0;
}
```

## Option 2: Reflection Bridge (No Hard Dependency)
Integrate with ModConfig without requiring it as a hard dependency. Prefer resolving the tree internally so callers do not need to thread a `SceneTree` through their own startup code:
```csharp
public static void TryRegisterWithModConfig()
{
    var tree = NGame.Instance?.GetTree();
    if (tree == null) return;

    var apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
    if (apiType == null) return;  // ModConfig not installed, gracefully skip

    // Defer 2 frames for ModConfig initialization
    tree.ProcessFrame += () => tree.ProcessFrame += () =>
    {
        var register = apiType.GetMethod("RegisterMod",
            BindingFlags.Static | BindingFlags.Public);
        // Call registration...
    };
}
```

`generate_settings_panel` follows this pattern. It emits a self-initializing helper and returns project-edit hints that add `ModSettings.Initialize();` to `ModEntry.Init()` rather than requiring a manual `SceneTree` parameter.

## Option 3: Manual JSON Config
```csharp
public static class MyConfig
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".sts2mods", "mymod", "config.json");

    public static bool FeatureEnabled { get; set; } = true;

    public static void Load()
    {
        if (!File.Exists(Path)) return;
        var json = File.ReadAllText(Path);
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        FeatureEnabled = data.TryGetValue("feature_enabled", out var v) && v.GetBoolean();
    }

    public static void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(new { feature_enabled = FeatureEnabled }));
    }
}
```

## Config Persistence Best Practices
- Save on value change (use debouncing for sliders)
- Load in ModEntry.Init()
- Use `%APPDATA%/.sts2mods/{mod_id}/` for storage
- Support config migration between versions
