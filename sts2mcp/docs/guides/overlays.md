# Creating Custom Overlays

## What Are Overlays?
Overlays are Godot `Control` nodes that inject into the game's scene tree via Harmony patches. They display custom UI elements (text, counters, debug info) on top of game screens like combat or the map. They update every frame and clean up automatically when the scene changes.

## How It Works
1. You create a `Control` subclass with your UI logic
2. A Harmony patch on the target scene's `_Ready()` adds your Control as a child node
3. Your `_Process(double delta)` method updates the display each frame
4. When the scene is freed (e.g., leaving combat), `_ExitTree()` cleans up

## Base Pattern
```csharp
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;

// The overlay node
public partial class MyOverlay : Control
{
    public static MyOverlay? Instance { get; private set; }

    private Label _label;

    public override void _Ready()
    {
        Instance = this;
        Name = "MyOverlay";

        // Position in top-right corner
        AnchorRight = 1f;
        OffsetLeft = -250;
        OffsetTop = 10;
        OffsetRight = -10;
        OffsetBottom = 60;

        _label = new Label();
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        _label.Text = GetDisplayText();
    }

    private string GetDisplayText()
    {
        // Read game state and format your display
        return "Custom info here";
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}

// Harmony patch to inject the overlay
[HarmonyPatch(typeof(NCombatRoom), "_Ready")]
public static class MyOverlayPatch
{
    public static void Postfix(NCombatRoom __instance)
    {
        if (MyOverlay.Instance != null) return;  // Prevent duplicates
        __instance.AddChild(new MyOverlay());
    }
}
```

## Injection Targets
Choose which scene to inject into based on when you want the overlay visible:

| Target Class | When Visible |
|-------------|-------------|
| `NCombatRoom` | During combat |
| `NMapRoom` | On the map screen |
| `NShopRoom` | In shops |
| `NRestSiteRoom` | At rest sites |
| `NEventRoom` | During events |

## Positioning
Use Godot's anchor/offset system to position your overlay:
```csharp
// Top-left
AnchorLeft = 0; AnchorTop = 0;
OffsetLeft = 10; OffsetTop = 10;

// Bottom-center
AnchorLeft = 0.5f; AnchorTop = 1f;
OffsetLeft = -100; OffsetTop = -50;

// Full-width bar at top
AnchorRight = 1f;
OffsetTop = 0; OffsetBottom = 40;
```

## Reading Game State
In `_Process()` or `GetDisplayText()`, access game state:
```csharp
// Combat state
var combat = CombatState.Instance;
var hp = combat?.Player?.Hp;
var enemies = combat?.Monsters;

// Run state
var run = RunManager.Instance;
var floor = run?.Floor;
var gold = run?.GetPlayer(0)?.Gold;
```

## Cleanup
The `_ExitTree()` method is called automatically when the parent scene is freed. Set `Instance = null` to allow re-injection in the next room. The static `Instance` check in the patch prevents duplicate overlays.

## Use Cases
- Combat stat trackers (damage dealt, cards played)
- Debug displays during mod development
- Custom HUD elements (timer, score counter)
- Visual indicators for mod-specific mechanics

## Generator
Use `generate_overlay` with a class name, mod_id, and injection target.
