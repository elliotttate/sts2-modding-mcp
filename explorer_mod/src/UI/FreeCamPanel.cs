using Godot;
using GodotExplorer.Core;

namespace GodotExplorer.UI;

/// <summary>
/// Small panel displaying freecam status and controls.
/// </summary>
public class FreeCamPanel
{
    public VBoxContainer Root { get; }

    private Button _toggleButton;
    private Label _positionLabel;
    private Label _zoomLabel;
    private HSlider _speedSlider;
    private FreeCamController? _controller;

    public FreeCamPanel()
    {
        Root = new VBoxContainer();
        Root.Name = "FreeCamPanel";
        Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Root.AddThemeConstantOverride("separation", ExplorerTheme.ItemSpacing);

        // Header
        var header = new Label();
        header.Text = "Free Camera (2D)";
        ExplorerTheme.StyleLabel(header, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeHeader);
        Root.AddChild(header);

        // Toggle button
        _toggleButton = new Button();
        _toggleButton.Text = "Enable Freecam";
        ExplorerTheme.StyleButton(_toggleButton);
        _toggleButton.Pressed += OnTogglePressed;
        Root.AddChild(_toggleButton);

        // Info labels
        _positionLabel = new Label();
        _positionLabel.Text = "Position: --";
        ExplorerTheme.StyleLabel(_positionLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        Root.AddChild(_positionLabel);

        _zoomLabel = new Label();
        _zoomLabel.Text = "Zoom: --";
        ExplorerTheme.StyleLabel(_zoomLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        Root.AddChild(_zoomLabel);

        // Speed control
        var speedRow = new HBoxContainer();
        speedRow.AddThemeConstantOverride("separation", 4);
        Root.AddChild(speedRow);

        var speedLabel = new Label();
        speedLabel.Text = "Speed:";
        ExplorerTheme.StyleLabel(speedLabel, ExplorerTheme.TextColor, ExplorerTheme.FontSizeSmall);
        speedRow.AddChild(speedLabel);

        _speedSlider = new HSlider();
        _speedSlider.MinValue = 50;
        _speedSlider.MaxValue = 2000;
        _speedSlider.Step = 50;
        _speedSlider.Value = 400;
        _speedSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _speedSlider.ValueChanged += OnSpeedChanged;
        speedRow.AddChild(_speedSlider);

        // Controls help
        var helpText = new Label();
        helpText.Text = "WASD: Pan | Scroll: Zoom | MMB Drag: Pan\nShift: Fast | Works while game runs";
        ExplorerTheme.StyleLabel(helpText, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        Root.AddChild(helpText);

        // Reset button
        var resetBtn = new Button();
        resetBtn.Text = "Reset Camera";
        ExplorerTheme.StyleButton(resetBtn);
        resetBtn.Pressed += OnResetPressed;
        Root.AddChild(resetBtn);

        // Set up controller when ready
        Root.Ready += () =>
        {
            _controller = new FreeCamController(ExplorerCore.SceneTree);
            _controller.ActiveChanged += OnActiveChanged;

            // Connect process frame for updates
            ExplorerCore.SceneTree?.Connect("process_frame", Callable.From(OnProcess));
        };
    }

    public FreeCamController? Controller => _controller;

    private void OnTogglePressed()
    {
        _controller?.Toggle();
    }

    private void OnActiveChanged(bool active)
    {
        _toggleButton.Text = active ? "Disable Freecam" : "Enable Freecam";
    }

    private void OnSpeedChanged(double value)
    {
        if (_controller != null)
            _controller.MoveSpeed = (float)value;
    }

    private void OnResetPressed()
    {
        if (_controller != null && _controller.IsActive)
        {
            _controller.Disable();
            _controller.Enable();
        }
    }

    private void OnProcess()
    {
        if (_controller == null || !_controller.IsActive) return;
        if (!ExplorerCore.IsVisible) return;

        _positionLabel.Text = $"Position: {_controller.Position:F1}";
        _zoomLabel.Text = $"Zoom: {_controller.Zoom:F2}";

        _controller.Process(ExplorerCore.SceneTree.Root.GetProcessDeltaTime());
    }
}
