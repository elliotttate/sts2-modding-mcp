using Godot;

namespace GodotExplorer.UI;

/// <summary>
/// Root UI container. Creates the CanvasLayer overlay and manages panel layout.
/// </summary>
public class ExplorerUI
{
    public CanvasLayer RootLayer { get; }

    // Root control covering the full viewport (mouse pass-through)
    private Control _rootControl;

    // Top toolbar
    private HBoxContainer _toolbar = null!;

    // Panel containers
    private PanelContainer _leftPanel = null!;
    private PanelContainer _rightPanel = null!;

    // Panels (populated in later phases)
    public SceneTreePanel? SceneTreePanel { get; private set; }
    public InspectorPanel? InspectorPanel { get; private set; }
    public SearchPanel? SearchPanel { get; private set; }
    public FreeCamPanel? FreeCamPanel { get; private set; }
    public ConsolePanel? ConsolePanel { get; private set; }

    // Track which panels are active
    private bool _searchVisible;
    private bool _consoleVisible;

    public ExplorerUI()
    {
        // Create the CanvasLayer that renders on top of everything
        RootLayer = new CanvasLayer();
        RootLayer.Name = "GodotExplorer";
        RootLayer.Layer = 128;

        // Root control: full-screen, passes mouse to game
        _rootControl = new Control();
        _rootControl.Name = "ExplorerRoot";
        _rootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _rootControl.MouseFilter = Control.MouseFilterEnum.Ignore;
        RootLayer.AddChild(_rootControl);

        BuildToolbar();
        BuildLeftPanel();
        BuildRightPanel();
        BuildBottomPanel();
    }

    private void BuildToolbar()
    {
        var toolbarBg = new PanelContainer();
        toolbarBg.Name = "ToolbarBg";
        toolbarBg.AddThemeStyleboxOverride("panel", ExplorerTheme.MakeFlatStyleBox(ExplorerTheme.TitleBarColor));
        toolbarBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        toolbarBg.CustomMinimumSize = new Vector2(0, 36);
        toolbarBg.MouseFilter = Control.MouseFilterEnum.Stop;
        _rootControl.AddChild(toolbarBg);

        _toolbar = new HBoxContainer();
        _toolbar.Name = "Toolbar";
        _toolbar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _toolbar.AddThemeConstantOverride("separation", 6);
        toolbarBg.AddChild(_toolbar);

        // Title
        var title = new Label();
        title.Text = $"  GodotExplorer v{GodotExplorer.Core.ExplorerCore.Version}";
        ExplorerTheme.StyleLabel(title, ExplorerTheme.AccentColor, ExplorerTheme.FontSizeHeader);
        title.CustomMinimumSize = new Vector2(200, 0);
        _toolbar.AddChild(title);

        // Spacer
        var spacer = new Control();
        spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _toolbar.AddChild(spacer);

        // Tab buttons
        AddToolbarButton("Scene Tree", true, () => ToggleLeftPanel(false));
        AddToolbarButton("Search", false, () => ToggleLeftPanel(true));

        // Mouse inspect button (highlight style when active)
        _inspectBtn = new Button();
        _inspectBtn.Text = "Inspect";
        _inspectBtn.TooltipText = "Click elements in the game to select them";
        _inspectBtn.ToggleMode = true;
        ExplorerTheme.StyleButton(_inspectBtn);
        _inspectBtn.Pressed += () => ToggleMouseInspect();
        _toolbar.AddChild(_inspectBtn);

        AddToolbarButton("Freecam", false, () => ToggleFreecam());
        AddToolbarButton("Console", false, () => ToggleConsole());

        // Spacer
        var spacer2 = new Control();
        spacer2.CustomMinimumSize = new Vector2(8, 0);
        _toolbar.AddChild(spacer2);

        // Close button
        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.TooltipText = "Close (F12)";
        ExplorerTheme.StyleButton(closeBtn);
        closeBtn.Pressed += () => GodotExplorer.Core.ExplorerCore.SetVisible(false);
        _toolbar.AddChild(closeBtn);
    }

    private void AddToolbarButton(string text, bool active, System.Action onPressed)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.ButtonPressed = active;
        ExplorerTheme.StyleButton(btn);
        btn.Pressed += onPressed;
        _toolbar.AddChild(btn);
    }

    private void BuildLeftPanel()
    {
        _leftPanel = new PanelContainer();
        _leftPanel.Name = "LeftPanel";
        _leftPanel.AddThemeStyleboxOverride("panel", ExplorerTheme.MakePanelStyleBox());
        _leftPanel.MouseFilter = Control.MouseFilterEnum.Stop;

        // Position: left side, below toolbar, ~320px wide
        _leftPanel.SetAnchor(Side.Left, 0);
        _leftPanel.SetAnchor(Side.Top, 0);
        _leftPanel.SetAnchor(Side.Right, 0);
        _leftPanel.SetAnchor(Side.Bottom, 1);
        _leftPanel.OffsetLeft = 4;
        _leftPanel.OffsetTop = 40;
        _leftPanel.OffsetRight = 324;
        _leftPanel.OffsetBottom = -4;

        _rootControl.AddChild(_leftPanel);

        // Create the scene tree panel
        SceneTreePanel = new SceneTreePanel();
        _leftPanel.AddChild(SceneTreePanel.Root);

        // Create search panel (hidden by default)
        SearchPanel = new SearchPanel();
        SearchPanel.Root.Visible = false;
        _leftPanel.AddChild(SearchPanel.Root);
    }

    private void BuildRightPanel()
    {
        _rightPanel = new PanelContainer();
        _rightPanel.Name = "RightPanel";
        _rightPanel.AddThemeStyleboxOverride("panel", ExplorerTheme.MakePanelStyleBox());
        _rightPanel.MouseFilter = Control.MouseFilterEnum.Stop;

        // Position: right side, below toolbar, ~380px wide
        _rightPanel.SetAnchor(Side.Left, 1);
        _rightPanel.SetAnchor(Side.Top, 0);
        _rightPanel.SetAnchor(Side.Right, 1);
        _rightPanel.SetAnchor(Side.Bottom, 1);
        _rightPanel.OffsetLeft = -384;
        _rightPanel.OffsetTop = 40;
        _rightPanel.OffsetRight = -4;
        _rightPanel.OffsetBottom = -4;

        _rootControl.AddChild(_rightPanel);

        // Create the inspector panel
        InspectorPanel = new InspectorPanel();
        _rightPanel.AddChild(InspectorPanel.Root);
    }

    private void ToggleLeftPanel(bool showSearch)
    {
        _searchVisible = showSearch;
        if (SceneTreePanel != null)
            SceneTreePanel.Root.Visible = !showSearch;
        if (SearchPanel != null)
            SearchPanel.Root.Visible = showSearch;
    }

    private void BuildBottomPanel()
    {
        // Console panel (bottom, initially hidden)
        _bottomPanel = new PanelContainer();
        _bottomPanel.Name = "BottomPanel";
        _bottomPanel.AddThemeStyleboxOverride("panel", ExplorerTheme.MakePanelStyleBox());
        _bottomPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _bottomPanel.Visible = false;

        // Position: bottom center, between left and right panels
        _bottomPanel.SetAnchor(Side.Left, 0);
        _bottomPanel.SetAnchor(Side.Top, 1);
        _bottomPanel.SetAnchor(Side.Right, 1);
        _bottomPanel.SetAnchor(Side.Bottom, 1);
        _bottomPanel.OffsetLeft = 328;
        _bottomPanel.OffsetTop = -254;
        _bottomPanel.OffsetRight = -388;
        _bottomPanel.OffsetBottom = -4;

        _rootControl.AddChild(_bottomPanel);

        ConsolePanel = new ConsolePanel();
        _bottomPanel.AddChild(ConsolePanel.Root);

        // Freecam panel (floating, hidden by default)
        FreeCamPanel = new FreeCamPanel();
        var freecamDraggable = new DraggablePanel("Free Camera", new Vector2(340, 50), new Vector2(280, 300));
        freecamDraggable.Content.AddChild(FreeCamPanel.Root);
        freecamDraggable.Root.Visible = false;
        _freecamContainer = freecamDraggable.Root;
        _rootControl.AddChild(_freecamContainer);

        // Sync inspect button when inspect mode is toggled externally
        var mi = GodotExplorer.Core.ExplorerCore.MouseInspect;
        if (mi != null)
        {
            mi.ActiveChanged += (active) =>
            {
                if (_inspectBtn != null)
                    _inspectBtn.ButtonPressed = active;
            };
        }
    }

    private PanelContainer _bottomPanel = null!;
    private PanelContainer _freecamContainer = null!;
    private Button _inspectBtn = null!;

    private void ToggleMouseInspect()
    {
        var mi = GodotExplorer.Core.ExplorerCore.MouseInspect;
        if (mi != null)
        {
            mi.Toggle();
            _inspectBtn.ButtonPressed = mi.IsActive;
        }
    }

    private void ToggleFreecam()
    {
        if (_freecamContainer != null)
            _freecamContainer.Visible = !_freecamContainer.Visible;
    }

    private void ToggleConsole()
    {
        _consoleVisible = !_consoleVisible;
        if (_bottomPanel != null)
            _bottomPanel.Visible = _consoleVisible;
    }

    /// <summary>
    /// Returns true if any text input field in the explorer has focus,
    /// meaning game input should be consumed.
    /// </summary>
    public bool HasFocusedInput()
    {
        return HasFocusedInputRecursive(_rootControl);
    }

    private static bool HasFocusedInputRecursive(Control control)
    {
        if (control is LineEdit le && le.HasFocus()) return true;
        if (control is TextEdit te && te.HasFocus()) return true;

        foreach (var child in control.GetChildren())
        {
            if (child is Control childControl && HasFocusedInputRecursive(childControl))
                return true;
        }
        return false;
    }
}
