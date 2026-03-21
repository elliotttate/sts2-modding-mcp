# Resource Loading Patterns

## PCK Resources (res:// paths)
Assets packed in your .pck file use `res://ModName/` paths:
```csharp
// Textures
var tex = GD.Load<Texture2D>("res://MyMod/images/relics/my_relic.png");
var tex2 = PreloadManager.Cache.GetTexture2D(
    ImageHelper.GetImagePath("res://MyMod/images/relics/my_relic.png"));

// Scenes
var scene = PreloadManager.Cache.GetScene("res://MyMod/scenes/my_scene.tscn");
var instance = scene.Instantiate();

// Generic resources
var resource = ResourceLoader.Load("res://MyMod/data/my_data.tres");
```

## DLL Embedded Resources
Load from the assembly manifest (no PCK required):
```csharp
var assembly = Assembly.GetExecutingAssembly();
using var stream = assembly.GetManifestResourceStream("MyMod.localization.en.json");
if (stream != null)
{
    using var reader = new StreamReader(stream);
    var json = reader.ReadToEnd();
}
```

## Fallback Chain Pattern
Try multiple sources with graceful degradation:
```csharp
public static Texture2D LoadTexture(string name)
{
    // Try PCK first
    var pckPath = $"res://MyMod/images/{name}";
    if (ResourceLoader.Exists(pckPath))
        return GD.Load<Texture2D>(pckPath);

    // Try local file
    var localPath = Path.Combine(OS.GetUserDataDir(), "mods", "MyMod", name);
    if (File.Exists(localPath))
    {
        var image = new Image();
        image.Load(localPath);
        return ImageTexture.CreateFromImage(image);
    }

    // Fallback to default
    return GD.Load<Texture2D>("res://default_icon.png");
}
```

## Intent Atlas Paths
```
res://atlases/intent_atlas.sprites/attack/intent_attack_1.tres  (< 5 damage)
res://atlases/intent_atlas.sprites/attack/intent_attack_2.tres  (5-9 damage)
res://atlases/intent_atlas.sprites/attack/intent_attack_3.tres  (10-19 damage)
res://atlases/intent_atlas.sprites/attack/intent_attack_4.tres  (20-29 damage)
res://atlases/intent_atlas.sprites/attack/intent_attack_5.tres  (30+ damage)
```

## Loading Custom Assemblies
For mods shipping NuGet dependencies:
```csharp
// In ModEntry, register assembly resolver BEFORE any code using the dependency:
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    var path = Path.Combine(modDir, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
```

## PCK Building
Use the `build_pck` MCP tool or build manually:
- PNGs are auto-converted to .ctex (Godot compressed texture)
- .import remap files are generated automatically
- Localization JSON, .tscn scenes, .tres resources pass through as-is
