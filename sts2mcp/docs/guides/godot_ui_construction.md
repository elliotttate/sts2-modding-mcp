# Programmatic Godot UI Construction

## Using .tscn Scene Files with Mod Scripts

If you create `.tscn` scenes in Godot that reference C# scripts from your mod, you **must** register your assembly during mod initialization:
```csharp
var assembly = Assembly.GetExecutingAssembly();
Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(assembly);
```
Without this, Godot won't find your mod's script classes when instantiating scenes. This is only needed when scene files reference your mod's scripts — purely programmatic UI doesn't need it.

## Creating Controls in C# (No .tscn Required)
Most STS2 mods build UI entirely in C# code, avoiding scene files.

## Common Pattern: Styled Panel
```csharp
public partial class MyPanel : Control
{
    public override void _Ready()
    {
        // Background
        var panel = new PanelContainer();
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.9f);
        style.BorderColor = new Color(0.8f, 0.7f, 0.3f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        style.SetContentMarginAll(12);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        // Layout container
        var vbox = new VBoxContainer();
        panel.AddChild(vbox);

        // Controls
        var label = new Label { Text = "Title" };
        label.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(label);

        var button = new Button { Text = "Click Me" };
        button.Pressed += OnButtonPressed;
        vbox.AddChild(button);
    }
}
```

## Injection Points
```csharp
// Add to game root (persistent):
NGame.Instance.AddChild(myPanel);

// Add to combat (cleanup automatic):
NCombatRoom.Instance.AddChild(myOverlay);

// Add via Harmony patch:
[HarmonyPatch(typeof(NMapScreen), "_Ready")]
static void Postfix(NMapScreen __instance) => __instance.AddChild(new MyPanel());
```

## Common Control Types
- `Label` - Text display
- `Button`, `TextureButton` - Clickable buttons
- `CheckButton` - Toggle switches
- `HSlider`, `VSlider` - Value sliders
- `LineEdit` - Text input
- `RichTextLabel` - Formatted text with BBCode
- `TextureRect` - Image display
- `VBoxContainer`, `HBoxContainer` - Layout containers
- `PanelContainer` - Background panel
- `MarginContainer` - Spacing

## Focus Chains (Controller/Keyboard Support)
```csharp
button1.FocusNeighborBottom = button2.GetPath();
button2.FocusNeighborTop = button1.GetPath();
button1.FocusMode = Control.FocusModeEnum.All;
```

## Hover Tooltips
```csharp
var tip = new HoverTip("Title", "Description text");
NHoverTipSet.CreateAndShow(tip, globalPosition);
```

## Tween Animations
```csharp
var tween = CreateTween();
tween.TweenProperty(node, "modulate:a", 1.0f, 0.3f);
tween.Parallel().TweenProperty(node, "scale", Vector2.One, 0.3f)
    .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
```

## Theme Overrides
```csharp
label.AddThemeColorOverride("font_color", new Color(1, 0.8f, 0.3f));
label.AddThemeFontSizeOverride("font_size", 16);
panel.AddThemeStyleboxOverride("panel", styleBox);
```
