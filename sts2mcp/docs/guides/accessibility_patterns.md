# Accessibility Patterns for STS2 Mods

## Focus Navigation
Ensure all interactive elements are keyboard/controller navigable:
```csharp
button.FocusMode = Control.FocusModeEnum.All;
button.FocusNeighborTop = previousButton.GetPath();
button.FocusNeighborBottom = nextButton.GetPath();
```

## Screen Reader Support
The say-the-spire2 mod shows how to add screen reader support:
```csharp
// Set accessible names on controls:
button.AccessibleName = "Attack button, deals 6 damage";
label.AccessibleDescription = "Player health: 65 of 80";
```

## TTS Integration (Windows)
```csharp
// Load System.Speech.dll from mod folder:
using System.Speech.Synthesis;

var synth = new SpeechSynthesizer();
synth.SetOutputToDefaultAudioDevice();
synth.SpeakAsync("Your turn. 3 cards in hand.");
```

## High Contrast / Visibility
```csharp
// Provide high-contrast color options:
var textColor = Settings.HighContrast
    ? new Color(1, 1, 1)        // Pure white
    : new Color(0.9f, 0.8f, 0.3f); // Gold

// Use outlines for text readability:
label.AddThemeConstantOverride("outline_size", 4);
label.AddThemeColorOverride("font_outline_color", Colors.Black);
```

## Input Alternatives
```csharp
// Support both mouse and keyboard for all actions:
public override void _Input(InputEvent @event)
{
    if (@event is InputEventKey key && key.Pressed && !key.Echo)
    {
        if (key.Keycode == Key.Z && key.CtrlPressed)
            UndoAction();
    }
}
```

## Best Practices
- Always set `FocusMode.All` on interactive controls
- Rebuild focus chains when adding/removing UI elements
- Provide text alternatives for icon-only buttons
- Support keyboard shortcuts for frequent actions
- Test with both mouse and controller input
- Consider color-blind friendly palettes (avoid red/green only distinctions)
