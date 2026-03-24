using Godot;

namespace GodotExplorer.UI;

/// <summary>
/// Dark theme constants and StyleBox factories for the explorer UI.
/// </summary>
public static class ExplorerTheme
{
    // Colors
    public static readonly Color BgColor = new(0.12f, 0.12f, 0.15f, 0.94f);
    public static readonly Color BgColorLight = new(0.16f, 0.16f, 0.20f, 0.96f);
    public static readonly Color BorderColor = new(0.30f, 0.30f, 0.35f, 1.0f);
    public static readonly Color AccentColor = new(0.35f, 0.55f, 0.95f, 1.0f);
    public static readonly Color AccentHover = new(0.45f, 0.65f, 1.0f, 1.0f);
    public static readonly Color TextColor = new(0.90f, 0.90f, 0.92f, 1.0f);
    public static readonly Color TextDim = new(0.60f, 0.60f, 0.65f, 1.0f);
    public static readonly Color TextHeader = new(0.75f, 0.85f, 1.0f, 1.0f);
    public static readonly Color ErrorColor = new(1.0f, 0.40f, 0.40f, 1.0f);
    public static readonly Color WarningColor = new(1.0f, 0.85f, 0.35f, 1.0f);
    public static readonly Color SuccessColor = new(0.40f, 0.90f, 0.50f, 1.0f);
    public static readonly Color ButtonNormal = new(0.20f, 0.20f, 0.25f, 1.0f);
    public static readonly Color ButtonHover = new(0.28f, 0.28f, 0.33f, 1.0f);
    public static readonly Color ButtonPressed = new(0.15f, 0.15f, 0.20f, 1.0f);
    public static readonly Color InputBg = new(0.08f, 0.08f, 0.10f, 1.0f);
    public static readonly Color TitleBarColor = new(0.10f, 0.10f, 0.13f, 0.98f);
    public static readonly Color SeparatorColor = new(0.25f, 0.25f, 0.30f, 1.0f);

    // Sizes
    public const int FontSizeSmall = 11;
    public const int FontSizeNormal = 13;
    public const int FontSizeHeader = 15;
    public const int CornerRadius = 4;
    public const int BorderWidth = 1;
    public const int PanelMargin = 8;
    public const int ItemSpacing = 4;

    public static StyleBoxFlat MakePanelStyleBox()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = BgColor;
        sb.SetCornerRadiusAll(CornerRadius);
        sb.SetBorderWidthAll(BorderWidth);
        sb.BorderColor = BorderColor;
        sb.SetContentMarginAll(PanelMargin);
        return sb;
    }

    public static StyleBoxFlat MakeTitleBarStyleBox()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = TitleBarColor;
        sb.CornerRadiusTopLeft = CornerRadius;
        sb.CornerRadiusTopRight = CornerRadius;
        sb.CornerRadiusBottomLeft = 0;
        sb.CornerRadiusBottomRight = 0;
        sb.SetBorderWidthAll(BorderWidth);
        sb.BorderColor = BorderColor;
        sb.SetContentMarginAll(4);
        return sb;
    }

    public static StyleBoxFlat MakeButtonStyleBox(Color bg)
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = bg;
        sb.SetCornerRadiusAll(3);
        sb.SetBorderWidthAll(1);
        sb.BorderColor = BorderColor;
        sb.SetContentMarginAll(4);
        sb.ContentMarginLeft = 8;
        sb.ContentMarginRight = 8;
        return sb;
    }

    public static StyleBoxFlat MakeInputStyleBox()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = InputBg;
        sb.SetCornerRadiusAll(3);
        sb.SetBorderWidthAll(1);
        sb.BorderColor = BorderColor;
        sb.SetContentMarginAll(4);
        return sb;
    }

    public static StyleBoxFlat MakeFlatStyleBox(Color bg)
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = bg;
        sb.SetCornerRadiusAll(0);
        sb.SetBorderWidthAll(0);
        sb.SetContentMarginAll(0);
        return sb;
    }

    public static StyleBoxFlat MakeSeparatorStyleBox()
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = SeparatorColor;
        sb.SetContentMarginAll(0);
        sb.ContentMarginTop = 1;
        sb.ContentMarginBottom = 1;
        return sb;
    }

    /// <summary>
    /// Apply the dark theme to a Button control.
    /// </summary>
    public static void StyleButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", MakeButtonStyleBox(ButtonNormal));
        button.AddThemeStyleboxOverride("hover", MakeButtonStyleBox(ButtonHover));
        button.AddThemeStyleboxOverride("pressed", MakeButtonStyleBox(ButtonPressed));
        button.AddThemeColorOverride("font_color", TextColor);
        button.AddThemeColorOverride("font_hover_color", AccentHover);
        button.AddThemeFontSizeOverride("font_size", FontSizeNormal);
    }

    /// <summary>
    /// Apply the dark theme to a LineEdit control.
    /// </summary>
    public static void StyleLineEdit(LineEdit lineEdit)
    {
        lineEdit.AddThemeStyleboxOverride("normal", MakeInputStyleBox());
        lineEdit.AddThemeStyleboxOverride("focus", MakeInputStyleBox());
        lineEdit.AddThemeColorOverride("font_color", TextColor);
        lineEdit.AddThemeColorOverride("font_placeholder_color", TextDim);
        lineEdit.AddThemeFontSizeOverride("font_size", FontSizeNormal);
    }

    /// <summary>
    /// Apply dark theme to a Tree control.
    /// </summary>
    public static void StyleTree(Tree tree)
    {
        var bgBox = MakeFlatStyleBox(new Color(0.09f, 0.09f, 0.11f, 0.96f));
        tree.AddThemeStyleboxOverride("panel", bgBox);

        var selectedBox = MakeFlatStyleBox(new Color(0.25f, 0.35f, 0.55f, 0.8f));
        tree.AddThemeStyleboxOverride("selected", selectedBox);
        tree.AddThemeStyleboxOverride("selected_focus", selectedBox);

        tree.AddThemeColorOverride("font_color", TextColor);
        tree.AddThemeColorOverride("font_selected_color", TextColor);
        tree.AddThemeFontSizeOverride("font_size", FontSizeNormal);
    }

    /// <summary>
    /// Apply dark theme to a Label.
    /// </summary>
    public static void StyleLabel(Label label, Color? color = null, int? fontSize = null)
    {
        label.AddThemeColorOverride("font_color", color ?? TextColor);
        label.AddThemeFontSizeOverride("font_size", fontSize ?? FontSizeNormal);
    }
}
