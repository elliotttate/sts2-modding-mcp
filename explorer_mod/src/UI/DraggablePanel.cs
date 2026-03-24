using Godot;

namespace GodotExplorer.UI;

/// <summary>
/// Base class for draggable, optionally resizable floating panels.
/// </summary>
public class DraggablePanel
{
    public PanelContainer Root { get; }
    public VBoxContainer Content { get; }

    private HBoxContainer _titleBar;
    private Label _titleLabel;
    private bool _dragging;
    private Vector2 _dragOffset;
    private bool _resizing;
    private Vector2 _resizeStartSize;
    private Vector2 _resizeStartMouse;

    public string Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    public DraggablePanel(string title, Vector2 position, Vector2 size, bool resizable = true)
    {
        Root = new PanelContainer();
        Root.Name = title.Replace(" ", "");
        Root.AddThemeStyleboxOverride("panel", ExplorerTheme.MakePanelStyleBox());
        Root.Position = position;
        Root.Size = size;
        Root.CustomMinimumSize = new Vector2(200, 150);
        Root.MouseFilter = Control.MouseFilterEnum.Stop;

        var outerVBox = new VBoxContainer();
        outerVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        outerVBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        outerVBox.AddThemeConstantOverride("separation", 2);
        Root.AddChild(outerVBox);

        // Title bar
        var titleBarPanel = new PanelContainer();
        titleBarPanel.AddThemeStyleboxOverride("panel", ExplorerTheme.MakeTitleBarStyleBox());
        titleBarPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        outerVBox.AddChild(titleBarPanel);

        _titleBar = new HBoxContainer();
        _titleBar.AddThemeConstantOverride("separation", 4);
        titleBarPanel.AddChild(_titleBar);

        _titleLabel = new Label();
        _titleLabel.Text = title;
        _titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ExplorerTheme.StyleLabel(_titleLabel, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeNormal);
        _titleBar.AddChild(_titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(24, 24);
        ExplorerTheme.StyleButton(closeBtn);
        closeBtn.Pressed += () => Root.Visible = false;
        _titleBar.AddChild(closeBtn);

        // Content area
        Content = new VBoxContainer();
        Content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Content.AddThemeConstantOverride("separation", ExplorerTheme.ItemSpacing);
        outerVBox.AddChild(Content);

        // Resize grip (if resizable)
        if (resizable)
        {
            var grip = new Label();
            grip.Text = "//";
            grip.HorizontalAlignment = HorizontalAlignment.Right;
            ExplorerTheme.StyleLabel(grip, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
            grip.MouseFilter = Control.MouseFilterEnum.Stop;
            grip.CustomMinimumSize = new Vector2(16, 16);
            outerVBox.AddChild(grip);

            grip.GuiInput += (ev) => HandleResizeInput(ev);
        }

        // Drag handling on title bar
        titleBarPanel.GuiInput += (ev) => HandleDragInput(ev);
    }

    private void HandleDragInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = mb.Pressed;
                _dragOffset = mb.GlobalPosition - Root.Position;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Root.Position = mm.GlobalPosition - _dragOffset;
        }
    }

    private void HandleResizeInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _resizing = mb.Pressed;
                _resizeStartSize = Root.Size;
                _resizeStartMouse = mb.GlobalPosition;
            }
        }
        else if (@event is InputEventMouseMotion mm && _resizing)
        {
            var delta = mm.GlobalPosition - _resizeStartMouse;
            var newSize = _resizeStartSize + delta;
            newSize.X = Mathf.Max(newSize.X, Root.CustomMinimumSize.X);
            newSize.Y = Mathf.Max(newSize.Y, Root.CustomMinimumSize.Y);
            Root.Size = newSize;
        }
    }
}
