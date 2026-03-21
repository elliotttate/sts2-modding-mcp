using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace {namespace}.UI;

/// <summary>
/// A mouse-following info panel with BBCode rich text support.
/// Press {hotkey} to toggle visibility.
/// </summary>
public partial class {class_name} : Control
{{
    public static {class_name}? Instance {{ get; private set; }}

    private PanelContainer _panel;
    private RichTextLabel _richText;
    private Label _header;
    private bool _isVisible;

    public override void _Ready()
    {{
        Instance = this;
        Name = "{class_name}";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2({panel_width}, 0);
        _panel.MouseFilter = MouseFilterEnum.Ignore;
        _panel.Visible = false;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.08f, 0.16f, 0.92f);
        style.BorderColor = new Color({border_color});
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(10);
        style.SetContentMarginAll(14);
        style.ShadowColor = new Color(0, 0, 0, 0.4f);
        style.ShadowSize = 4;
        _panel.AddThemeStyleboxOverride("panel", style);
        AddChild(_panel);

        var vbox = new VBoxContainer();
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(vbox);

        _header = new Label();
        _header.Text = "{panel_title}";
        _header.AddThemeFontSizeOverride("font_size", 15);
        _header.AddThemeColorOverride("font_color", new Color({header_color}));
        _header.HorizontalAlignment = HorizontalAlignment.Center;
        _header.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(_header);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        _richText = new RichTextLabel();
        _richText.BbcodeEnabled = true;
        _richText.FitContent = true;
        _richText.ScrollActive = false;
        _richText.CustomMinimumSize = new Vector2({panel_width} - 30, 0);
        _richText.AddThemeFontSizeOverride("normal_font_size", 13);
        _richText.AddThemeColorOverride("default_color", new Color(0.85f, 0.85f, 0.9f));
        _richText.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(_richText);

        SetContent("{panel_title}", "{initial_content}");
    }}

    public override void _Input(InputEvent @event)
    {{
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.{hotkey})
        {{
            if (_isVisible) HidePanel(); else ShowPanel();
        }}
    }}

    public override void _Process(double delta)
    {{
        if (!_isVisible) return;
        var mouse = GetGlobalMousePosition();
        var size = _panel.Size;
        var vp = GetViewportRect().Size;
        float x = mouse.X + {offset_x};
        float y = mouse.Y + {offset_y};
        if (x + size.X > vp.X - 10) x = mouse.X - size.X - 10;
        if (y + size.Y > vp.Y - 10) y = mouse.Y - size.Y - 10;
        _panel.Position = new Vector2(x, y);
    }}

    public void SetContent(string header, string bbcodeBody)
    {{
        _header.Text = header;
        _richText.Text = bbcodeBody;
    }}

    private void ShowPanel()
    {{
        _isVisible = true;
        _panel.Visible = true;
        _panel.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(_panel, "modulate:a", 1.0f, {fade_duration}f)
            .SetEase(Tween.EaseType.Out);
    }}

    private void HidePanel()
    {{
        _isVisible = false;
        var tween = CreateTween();
        tween.TweenProperty(_panel, "modulate:a", 0.0f, {fade_duration}f)
            .SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => _panel.Visible = false));
    }}

    public override void _ExitTree()
    {{
        if (Instance == this) Instance = null;
    }}
}}

[HarmonyPatch(typeof({patch_target}), "_Ready")]
public static class {class_name}Patch
{{
    public static void Postfix({patch_target} __instance)
    {{
        if ({class_name}.Instance != null) return;
        __instance.AddChild(new {class_name}());
        Log.Info("[{mod_id}] Floating panel injected.");
    }}
}}
