using Godot;
using MultiplayerAwards.Awards;

namespace MultiplayerAwards.UI;

public partial class AwardCard : PanelContainer
{
    private static readonly Color OffenseColor = new Color(0.85f, 0.65f, 0.13f, 1f);   // Gold
    private static readonly Color DefenseColor = new Color(0.27f, 0.51f, 0.71f, 1f);   // Blue
    private static readonly Color SupportColor = new Color(0.30f, 0.69f, 0.31f, 1f);   // Green
    private static readonly Color EfficiencyColor = new Color(0.75f, 0.75f, 0.75f, 1f); // Silver
    private static readonly Color FunnyColor = new Color(0.61f, 0.35f, 0.71f, 1f);      // Purple
    private static readonly Color ParticipationColor = new Color(0.93f, 0.86f, 0.51f, 1f); // Light gold

    public AwardResult? Result { get; private set; }
    public string DisplayPlayerName { get; private set; } = "";

    public static AwardCard Create(AwardResult result, string playerDisplayName)
    {
        var card = new AwardCard();
        card.Result = result;
        card.DisplayPlayerName = playerDisplayName;
        card.SetupUI();
        return card;
    }

    private void SetupUI()
    {
        if (Result == null) return;

        CustomMinimumSize = new Vector2(220, 100);

        // Background style
        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = new Color(0.10f, 0.10f, 0.14f, 0.95f);
        stylebox.BorderColor = GetCategoryColor(Result.Award.Category);
        stylebox.SetBorderWidthAll(2);
        stylebox.SetCornerRadiusAll(6);
        stylebox.SetContentMarginAll(10);
        AddThemeStyleboxOverride("panel", stylebox);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);

        // Award title in category color
        var titleLabel = new Label();
        titleLabel.Text = Result.Award.Title;
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeColorOverride("font_color", GetCategoryColor(Result.Award.Category));
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(titleLabel);

        // Stat value (big number)
        if (!string.IsNullOrEmpty(Result.DisplayValue))
        {
            var valueLabel = new Label();
            valueLabel.Text = Result.DisplayValue;
            valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
            valueLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            valueLabel.AddThemeFontSizeOverride("font_size", 20);
            vbox.AddChild(valueLabel);
        }

        // Description
        var descLabel = new Label();
        descLabel.Text = Result.Description;
        descLabel.HorizontalAlignment = HorizontalAlignment.Center;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        descLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        descLabel.AddThemeFontSizeOverride("font_size", 10);
        vbox.AddChild(descLabel);

        AddChild(vbox);

        // Start invisible for animation
        Modulate = new Color(1, 1, 1, 0);
        Scale = new Vector2(0.8f, 0.8f);
        PivotOffset = CustomMinimumSize / 2f;
    }

    public Tween AnimateIn(float delay)
    {
        var tween = CreateTween();
        tween.SetParallel(true);

        tween.TweenProperty(this, "modulate:a", 1.0f, 0.4f)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);

        tween.TweenProperty(this, "scale", Vector2.One, 0.5f)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);

        return tween;
    }

    public static Color GetCategoryColor(AwardCategory category)
    {
        return category switch
        {
            AwardCategory.Offense => OffenseColor,
            AwardCategory.Defense => DefenseColor,
            AwardCategory.Support => SupportColor,
            AwardCategory.Efficiency => EfficiencyColor,
            AwardCategory.Funny => FunnyColor,
            AwardCategory.Participation => ParticipationColor,
            _ => new Color(1, 1, 1)
        };
    }
}
