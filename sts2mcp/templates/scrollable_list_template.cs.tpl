using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace {namespace}.UI;

/// <summary>
/// Scrollable list panel with dynamic item entries, color-coded rows, and slide animation.
/// Press {hotkey} to toggle the panel.
/// </summary>
public partial class {class_name} : Control
{{
    public static {class_name}? Instance {{ get; private set; }}

    private PanelContainer _panel;
    private VBoxContainer _itemList;
    private Label _headerLabel;
    private Label _countLabel;
    private bool _isOpen;
    private bool _isAnimating;
    private const float PanelWidth = {panel_width}f;
    private const float ClosedOffset = {panel_width}f + 10f;

    public override void _Ready()
    {{
        Instance = this;
        Name = "{class_name}";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2(PanelWidth, 0);
        _panel.AnchorLeft = 1f;
        _panel.AnchorRight = 1f;
        _panel.AnchorTop = 0f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -PanelWidth;
        _panel.OffsetRight = 0;
        _panel.OffsetTop = 75;
        _panel.OffsetBottom = -10;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.13f, 0.9f);
        style.BorderColor = new Color({border_color});
        style.BorderWidthLeft = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthBottom = 2;
        style.CornerRadiusTopLeft = 10;
        style.CornerRadiusBottomLeft = 10;
        style.SetContentMarginAll(10);
        _panel.AddThemeStyleboxOverride("panel", style);
        _panel.Position = new Vector2(ClosedOffset, 0);
        AddChild(_panel);

        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(mainVbox);

        var headerRow = new HBoxContainer();
        mainVbox.AddChild(headerRow);

        _headerLabel = new Label();
        _headerLabel.Text = "{list_title}";
        _headerLabel.AddThemeFontSizeOverride("font_size", 15);
        _headerLabel.AddThemeColorOverride("font_color", new Color({header_color}));
        _headerLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(_headerLabel);

        _countLabel = new Label();
        _countLabel.Text = "(0)";
        _countLabel.AddThemeFontSizeOverride("font_size", 13);
        _countLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
        headerRow.AddChild(_countLabel);

        var sep = new HSeparator();
        mainVbox.AddChild(sep);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        mainVbox.AddChild(scroll);

        _itemList = new VBoxContainer();
        _itemList.AddThemeConstantOverride("separation", 2);
        _itemList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_itemList);
    }}

    public override void _Input(InputEvent @event)
    {{
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.{hotkey})
        {{
            Toggle();
        }}
    }}

    /// <summary>Add an item row with optional color indicator dot.</summary>
    public void AddItem(string text, Color? dotColor = null, string badge = "")
    {{
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        if (dotColor.HasValue)
        {{
            var dot = new ColorRect();
            dot.CustomMinimumSize = new Vector2(8, 8);
            dot.Color = dotColor.Value;
            dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(dot);
        }}

        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.82f));
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.ClipText = true;
        row.AddChild(label);

        if (!string.IsNullOrEmpty(badge))
        {{
            var badgeLabel = new Label();
            badgeLabel.Text = badge;
            badgeLabel.AddThemeFontSizeOverride("font_size", 11);
            badgeLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.9f));
            row.AddChild(badgeLabel);
        }}

        _itemList.AddChild(row);
        _countLabel.Text = $"({{_itemList.GetChildCount()}})";
    }}

    /// <summary>Remove all list items.</summary>
    public void ClearItems()
    {{
        foreach (var child in _itemList.GetChildren())
            child.QueueFree();
        _countLabel.Text = "(0)";
    }}

    public void Toggle()
    {{
        if (_isAnimating) return;
        if (_isOpen) SlideOut(); else SlideIn();
    }}

    private void SlideIn()
    {{
        _isOpen = true;
        _isAnimating = true;
        var tween = CreateTween();
        tween.TweenProperty(_panel, "position:x", 0f, 0.3f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenCallback(Callable.From(() => _isAnimating = false));
    }}

    private void SlideOut()
    {{
        _isOpen = false;
        _isAnimating = true;
        var tween = CreateTween();
        tween.TweenProperty(_panel, "position:x", ClosedOffset, 0.25f)
            .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenCallback(Callable.From(() => _isAnimating = false));
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
        Log.Info("[{mod_id}] Scrollable list injected.");
    }}
}}
