using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace {namespace}.UI;

/// <summary>
/// Animated progress bar with smooth tweens, color gradients, and optional low-value pulse.
/// </summary>
public partial class {class_name} : Control
{{
    public static {class_name}? Instance {{ get; private set; }}

    private ProgressBar _bar;
    private Label _label;
    private PanelContainer _container;
    private Tween? _pulseTween;
    private bool _isPulsing;
    private float _lastRatio = 1f;

    private static readonly Color ColorLow = new Color({color_low});
    private static readonly Color ColorHigh = new Color({color_high});

    public override void _Ready()
    {{
        Instance = this;
        Name = "{class_name}";
        MouseFilter = MouseFilterEnum.Ignore;

        _container = new PanelContainer();
        _container.MouseFilter = MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.8f);
        style.BorderColor = new Color(0.3f, 0.6f, 0.9f, 0.5f);
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        style.SetContentMarginAll(8);
        _container.AddThemeStyleboxOverride("panel", style);
        AddChild(_container);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        _container.AddChild(vbox);

        _label = new Label();
        _label.Text = "{bar_label}: -- / --";
        _label.AddThemeFontSizeOverride("font_size", 12);
        _label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.85f));
        _label.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(_label);

        _bar = new ProgressBar();
        _bar.CustomMinimumSize = new Vector2({bar_width}, {bar_height});
        _bar.ShowPercentage = false;
        _bar.MouseFilter = MouseFilterEnum.Ignore;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.15f, 0.15f, 0.2f);
        bg.SetCornerRadiusAll(4);
        _bar.AddThemeStyleboxOverride("background", bg);

        var fill = new StyleBoxFlat();
        fill.BgColor = ColorHigh;
        fill.SetCornerRadiusAll(4);
        _bar.AddThemeStyleboxOverride("fill", fill);
        vbox.AddChild(_bar);
    }}

    /// <summary>Update the bar with smooth animation and color interpolation.</summary>
    public void SetValue(float current, float max)
    {{
        if (max <= 0) return;
        _bar.MaxValue = max;
        _label.Text = $"{{"{bar_label}"}}: {{(int)current}} / {{(int)max}}";

        var tween = CreateTween();
        tween.TweenProperty(_bar, "value", (double)current, 0.4f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);

        float ratio = current / max;
        Color barColor = ColorHigh.Lerp(ColorLow, 1f - ratio);
        var fillStyle = new StyleBoxFlat();
        fillStyle.BgColor = barColor;
        fillStyle.SetCornerRadiusAll(4);
        _bar.AddThemeStyleboxOverride("fill", fillStyle);

        // Flash white on decrease
        if (ratio < _lastRatio - 0.01f)
        {{
            var flash = CreateTween();
            flash.TweenProperty(_bar, "modulate", new Color(2f, 2f, 2f, 1f), 0.05f);
            flash.TweenProperty(_bar, "modulate", Colors.White, 0.25f);
        }}

        // Pulse when low
        if ({pulse_enabled} && ratio <= 0.3f && !_isPulsing)
            StartPulse();
        else if (ratio > 0.3f && _isPulsing)
            StopPulse();

        _lastRatio = ratio;
    }}

    private void StartPulse()
    {{
        _isPulsing = true;
        _pulseTween?.Kill();
        _pulseTween = CreateTween();
        _pulseTween.SetLoops();
        _pulseTween.TweenProperty(_container, "modulate:a", 0.5f, 0.5f)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        _pulseTween.TweenProperty(_container, "modulate:a", 1.0f, 0.5f)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
    }}

    private void StopPulse()
    {{
        _isPulsing = false;
        _pulseTween?.Kill();
        _container.Modulate = Colors.White;
    }}

    public override void _ExitTree()
    {{
        _pulseTween?.Kill();
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
        Log.Info("[{mod_id}] Animated bar injected.");
    }}
}}
