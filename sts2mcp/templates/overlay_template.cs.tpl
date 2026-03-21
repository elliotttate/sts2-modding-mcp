using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace {namespace}.Overlays;

/// <summary>
/// {overlay_description}
/// Automatically injected into the {inject_target} scene tree.
/// </summary>
public partial class {class_name} : Control
{{
    public static {class_name}? Instance {{ get; private set; }}

    private Label _label;

    public override void _Ready()
    {{
        Instance = this;
        Name = "{class_name}";

        // Position in top-right by default
        AnchorRight = 1f;
        OffsetLeft = -250;
        OffsetTop = 10;
        OffsetRight = -10;
        OffsetBottom = 60;

        _label = new Label();
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
        AddChild(_label);
    }}

    public override void _Process(double delta)
    {{
        // Update overlay content each frame
        _label.Text = GetDisplayText();
    }}

    private string GetDisplayText()
    {{
        // TODO: Return your overlay text
        return "{class_name} active";
    }}

    public override void _ExitTree()
    {{
        if (Instance == this) Instance = null;
    }}
}}

/// <summary>
/// Harmony patch to inject the overlay into the game scene.
/// </summary>
[HarmonyPatch(typeof({patch_target}), "_Ready")]
public static class {class_name}Patch
{{
    public static void Postfix({patch_target} __instance)
    {{
        if ({class_name}.Instance != null) return;

        var overlay = new {class_name}();
        __instance.AddChild(overlay);
        Log.Info("[{mod_id}] Overlay injected.");
    }}
}}
