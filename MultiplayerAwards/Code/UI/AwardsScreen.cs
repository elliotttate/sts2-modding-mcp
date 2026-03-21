using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MultiplayerAwards.Awards;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.UI;

public partial class AwardsScreen : Control
{
    private static AwardsScreen? _instance;

    public static new bool IsVisible => _instance != null;

    public static void CloseIfOpen()
    {
        _instance?.OnClosePressed();
    }

    // Player column colors (one per player slot)
    private static readonly Color[] PlayerColors = new[]
    {
        new Color(0.85f, 0.45f, 0.45f, 1f), // Red
        new Color(0.45f, 0.65f, 0.85f, 1f), // Blue
        new Color(0.45f, 0.80f, 0.45f, 1f), // Green
        new Color(0.85f, 0.75f, 0.40f, 1f), // Yellow
    };

    private PanelContainer? _panel;
    private HBoxContainer? _playersRow;

    public static void ShowAwards(List<AwardResult> awards, IReadOnlyDictionary<ulong, PlayerRunStats> stats)
    {
        try
        {
            if (_instance != null)
            {
                _instance.GetParent()?.QueueFree();
                _instance = null;
            }

            var root = Engine.GetMainLoop() as SceneTree;
            var scene = root?.CurrentScene;
            if (scene == null)
            {
                Log.Error("[MultiplayerAwards] Cannot find scene root to attach awards screen.");
                return;
            }

            var layer = new CanvasLayer();
            layer.Layer = 100;
            scene.AddChild(layer);

            var screen = new AwardsScreen();
            screen.SetAnchorsPreset(LayoutPreset.FullRect);
            screen.MouseFilter = MouseFilterEnum.Stop;
            layer.AddChild(screen);
            _instance = screen;

            screen.BuildUI(awards, stats);
            screen.AnimateIn();

            Log.Info($"[MultiplayerAwards] Awards screen displayed with {awards.Count} awards for {stats.Count} players.");
        }
        catch (System.Exception ex)
        {
            Log.Error($"[MultiplayerAwards] ShowAwards UI error: {ex}");
        }
    }

    public override void _Ready()
    {
        SetProcessInput(true);
        SetProcessUnhandledInput(true);
    }

    private void BuildUI(List<AwardResult> awards, IReadOnlyDictionary<ulong, PlayerRunStats> stats)
    {
        // Dark background — clicking dismisses
        var bg = new Button();
        bg.Flat = true;
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = MouseFilterEnum.Stop;
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.75f) };
        bg.AddThemeStyleboxOverride("normal", bgStyle);
        bg.AddThemeStyleboxOverride("hover", bgStyle);
        bg.AddThemeStyleboxOverride("pressed", bgStyle);
        bg.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        bg.Pressed += OnClosePressed;
        AddChild(bg);

        // Main panel
        _panel = new PanelContainer();
        _panel.SetAnchorsPreset(LayoutPreset.Center);
        _panel.GrowHorizontal = GrowDirection.Both;
        _panel.GrowVertical = GrowDirection.Both;
        _panel.MouseFilter = MouseFilterEnum.Stop;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.98f);
        panelStyle.BorderColor = new Color(0.85f, 0.65f, 0.13f, 0.8f);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.SetCornerRadiusAll(12);
        panelStyle.SetContentMarginAll(20);
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_panel);

        // Main layout
        var mainVBox = new VBoxContainer();
        mainVBox.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(mainVBox);

        // Title
        var titleLabel = new Label();
        titleLabel.Text = "MULTIPLAYER AWARDS";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.65f, 0.13f));
        titleLabel.AddThemeFontSizeOverride("font_size", 28);
        mainVBox.AddChild(titleLabel);

        // Subtitle
        var subtitleLabel = new Label();
        subtitleLabel.Text = "Every player's awards from this run";
        subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        subtitleLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        subtitleLabel.AddThemeFontSizeOverride("font_size", 13);
        mainVBox.AddChild(subtitleLabel);

        var titleSep = new HSeparator();
        mainVBox.AddChild(titleSep);

        // Scrollable area
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(850, 450);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        scroll.MouseFilter = MouseFilterEnum.Stop;
        mainVBox.AddChild(scroll);

        // Player columns side by side
        _playersRow = new HBoxContainer();
        _playersRow.AddThemeConstantOverride("separation", 16);
        _playersRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        scroll.AddChild(_playersRow);

        // Group awards by player
        var awardsByPlayer = new Dictionary<ulong, List<AwardResult>>();
        foreach (var s in stats)
            awardsByPlayer[s.Key] = new List<AwardResult>();
        foreach (var award in awards)
        {
            if (awardsByPlayer.ContainsKey(award.WinnerNetId))
                awardsByPlayer[award.WinnerNetId].Add(award);
        }

        // Build a column for each player
        int playerIndex = 0;
        foreach (var (netId, playerStats) in stats)
        {
            var playerAwards = awardsByPlayer.GetValueOrDefault(netId, new List<AwardResult>());
            var playerColor = PlayerColors[playerIndex % PlayerColors.Length];
            var displayName = !string.IsNullOrEmpty(playerStats.PlayerDisplayName)
                ? playerStats.PlayerDisplayName
                : playerStats.CharacterName;

            var column = BuildPlayerColumn(displayName, playerStats.CharacterName, playerColor, playerAwards, playerIndex);
            _playersRow.AddChild(column);
            playerIndex++;
        }

        // Close button
        var closeButton = new Button();
        closeButton.Text = "Close";
        closeButton.CustomMinimumSize = new Vector2(140, 44);
        closeButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        closeButton.FocusMode = FocusModeEnum.All;
        closeButton.MouseFilter = MouseFilterEnum.Stop;

        var buttonStyle = new StyleBoxFlat();
        buttonStyle.BgColor = new Color(0.25f, 0.22f, 0.15f, 1f);
        buttonStyle.BorderColor = new Color(0.85f, 0.65f, 0.13f, 0.8f);
        buttonStyle.SetBorderWidthAll(2);
        buttonStyle.SetCornerRadiusAll(6);
        buttonStyle.SetContentMarginAll(8);
        closeButton.AddThemeStyleboxOverride("normal", buttonStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.4f, 0.35f, 0.2f, 1f);
        hoverStyle.BorderColor = new Color(0.95f, 0.75f, 0.2f, 1f);
        hoverStyle.SetBorderWidthAll(2);
        hoverStyle.SetCornerRadiusAll(6);
        hoverStyle.SetContentMarginAll(8);
        closeButton.AddThemeStyleboxOverride("hover", hoverStyle);

        closeButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        closeButton.AddThemeFontSizeOverride("font_size", 16);
        closeButton.Pressed += OnClosePressed;
        mainVBox.AddChild(closeButton);

        // Start invisible
        _panel.Modulate = new Color(1, 1, 1, 0);
        _panel.Scale = new Vector2(0.9f, 0.9f);
        _panel.PivotOffset = _panel.Size / 2f;
    }

    private PanelContainer BuildPlayerColumn(string displayName, string characterName,
        Color playerColor, List<AwardResult> playerAwards, int playerIndex)
    {
        var column = new PanelContainer();
        column.CustomMinimumSize = new Vector2(200, 0);
        column.SizeFlagsVertical = SizeFlags.ShrinkBegin;

        // Column background
        var colStyle = new StyleBoxFlat();
        colStyle.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.9f);
        colStyle.BorderColor = playerColor;
        colStyle.SetBorderWidthAll(2);
        colStyle.SetCornerRadiusAll(8);
        colStyle.SetContentMarginAll(12);
        // Thicker top border for player identity
        colStyle.BorderWidthTop = 4;
        column.AddThemeStyleboxOverride("panel", colStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);

        // Player display name (big, colored)
        var nameLabel = new Label();
        nameLabel.Text = displayName;
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.AddThemeColorOverride("font_color", playerColor);
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(nameLabel);

        // Character class (smaller, gray)
        if (characterName != displayName)
        {
            var classLabel = new Label();
            classLabel.Text = characterName;
            classLabel.HorizontalAlignment = HorizontalAlignment.Center;
            classLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            classLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(classLabel);
        }

        // Separator
        var sep = new HSeparator();
        vbox.AddChild(sep);

        // Award count badge
        var countLabel = new Label();
        countLabel.Text = playerAwards.Count == 1
            ? "1 Award"
            : $"{playerAwards.Count} Awards";
        countLabel.HorizontalAlignment = HorizontalAlignment.Center;
        countLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        countLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(countLabel);

        // Award cards stacked vertically
        foreach (var award in playerAwards)
        {
            var card = AwardCard.Create(award, displayName);
            vbox.AddChild(card);
        }

        // If no awards, show a participation message
        if (playerAwards.Count == 0)
        {
            var noAwardsLabel = new Label();
            noAwardsLabel.Text = "No special awards\nthis run";
            noAwardsLabel.HorizontalAlignment = HorizontalAlignment.Center;
            noAwardsLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.4f));
            noAwardsLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(noAwardsLabel);
        }

        column.AddChild(vbox);

        // Start invisible for staggered animation
        column.Modulate = new Color(1, 1, 1, 0);

        return column;
    }

    private void AnimateIn()
    {
        if (_panel == null) return;

        var tween = CreateTween();

        // Panel fade + scale
        tween.TweenProperty(_panel, "modulate:a", 1.0f, 0.5f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);

        tween.Parallel().TweenProperty(_panel, "scale", Vector2.One, 0.5f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);

        // Animate each player column with stagger
        if (_playersRow != null)
        {
            float columnDelay = 0.5f;
            foreach (var child in _playersRow.GetChildren())
            {
                if (child is PanelContainer column)
                {
                    var colTween = CreateTween();
                    colTween.TweenProperty(column, "modulate:a", 1.0f, 0.4f)
                        .SetDelay(columnDelay)
                        .SetTrans(Tween.TransitionType.Cubic)
                        .SetEase(Tween.EaseType.Out);

                    // Animate award cards inside this column
                    float cardDelay = columnDelay + 0.3f;
                    var vbox = column.GetChildOrNull<VBoxContainer>(0);
                    if (vbox != null)
                    {
                        foreach (var vboxChild in vbox.GetChildren())
                        {
                            if (vboxChild is AwardCard card)
                            {
                                card.AnimateIn(cardDelay);
                                cardDelay += 0.15f;
                            }
                        }
                    }

                    columnDelay += 0.3f;
                }
            }
        }
    }

    private void OnClosePressed()
    {
        if (_instance == null) return;
        _instance = null;

        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.3f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() =>
        {
            GetParent()?.QueueFree();
        }));
    }

    public override void _Input(InputEvent @event)
    {
        // Consume ALL input so the game doesn't react behind us
        GetViewport().SetInputAsHandled();

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.Escape)
        {
            OnClosePressed();
        }
    }
}
