using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace}.UI;

/// <summary>
/// Custom UI panel. Add to scene tree in ModEntry or via Harmony patch:
///   NGame.Instance.AddChild(new {class_name}());
/// </summary>
public partial class {class_name} : {base_type}
{{
    private Label _titleLabel;
    private VBoxContainer _container;
{extra_fields}

    public override void _Ready()
    {{
        // Configure root control
        Name = "{class_name}";
{position_setup}

        // Background panel
        var panel = new PanelContainer();
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.9f);
        style.BorderColor = new Color(0.8f, 0.7f, 0.3f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        style.SetContentMarginAll(12);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        _container = new VBoxContainer();
        _container.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(_container);

        _titleLabel = new Label();
        _titleLabel.Text = "{title}";
        _titleLabel.AddThemeFontSizeOverride("font_size", 20);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.3f));
        _container.AddChild(_titleLabel);

{controls_init}
    }}
{process_body}
}}
