using Godot;
using GodotExplorer.Core;
using System.Collections.Generic;

namespace GodotExplorer.UI;

/// <summary>
/// Property inspector panel. Shows all editable properties of the selected node
/// with type-appropriate editor widgets.
/// </summary>
public class InspectorPanel
{
    public VBoxContainer Root { get; }

    private Label _headerLabel;
    private Label _typeLabel;
    private Label _pathLabel;
    private ScrollContainer _scrollContainer;
    private VBoxContainer _propertiesContainer;

    // Track which node we're inspecting (by instance ID)
    private ulong _inspectedNodeId;
    private readonly List<PropertyRow> _propertyRows = new();

    // Auto-refresh state
    private double _refreshTimer;
    private const double RefreshInterval = 0.5;

    private record PropertyRow(string Name, Control Editor, Variant.Type Type);

    public InspectorPanel()
    {
        Root = new VBoxContainer();
        Root.Name = "InspectorPanel";
        Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Root.AddThemeConstantOverride("separation", ExplorerTheme.ItemSpacing);

        // Header section
        _headerLabel = new Label();
        _headerLabel.Text = "Inspector";
        ExplorerTheme.StyleLabel(_headerLabel, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeHeader);
        Root.AddChild(_headerLabel);

        _typeLabel = new Label();
        ExplorerTheme.StyleLabel(_typeLabel, ExplorerTheme.AccentColor, ExplorerTheme.FontSizeNormal);
        Root.AddChild(_typeLabel);

        _pathLabel = new Label();
        _pathLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        ExplorerTheme.StyleLabel(_pathLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        Root.AddChild(_pathLabel);

        // Separator
        var sep = new HSeparator();
        Root.AddChild(sep);

        // Scrollable properties area
        _scrollContainer = new ScrollContainer();
        _scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        Root.AddChild(_scrollContainer);

        _propertiesContainer = new VBoxContainer();
        _propertiesContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _propertiesContainer.AddThemeConstantOverride("separation", 2);
        _scrollContainer.AddChild(_propertiesContainer);

        // Placeholder text
        var placeholder = new Label();
        placeholder.Text = "Select a node to inspect its properties.";
        ExplorerTheme.StyleLabel(placeholder, ExplorerTheme.TextDim);
        _propertiesContainer.AddChild(placeholder);

        // Listen for node selection changes
        ExplorerCore.NodeSelected += OnNodeSelected;

        // Set up process for auto-refresh
        Root.Ready += () =>
        {
            Root.SetProcess(true);
            Root.GetTree()?.Connect("process_frame", Callable.From(() => OnProcess()));
        };
    }

    private void OnNodeSelected(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            ClearInspector("No node selected.");
            _inspectedNodeId = 0;
            return;
        }

        _inspectedNodeId = node.GetInstanceId();
        BuildInspector(node);
    }

    private void BuildInspector(Node node)
    {
        // Update header
        _headerLabel.Text = node.Name;
        _typeLabel.Text = node.GetClass();
        _pathLabel.Text = node.GetPath().ToString();

        // Clear existing properties
        ClearProperties();

        var properties = PropertyHelper.GetProperties(node);
        string lastCategory = "";

        foreach (var prop in properties)
        {
            if (PropertyHelper.IsCategoryMarker(prop))
            {
                if (prop.Name != lastCategory)
                {
                    lastCategory = prop.Name;
                    AddCategoryHeader(prop.Name);
                }
                continue;
            }

            if (PropertyHelper.IsGroupMarker(prop) || PropertyHelper.IsSubgroupMarker(prop))
            {
                AddGroupHeader(prop.Name);
                continue;
            }

            AddPropertyRow(node, prop);
        }
    }

    private void AddCategoryHeader(string name)
    {
        var header = new Label();
        header.Text = name;
        ExplorerTheme.StyleLabel(header, ExplorerTheme.AccentColor, ExplorerTheme.FontSizeNormal);

        var sep = new HSeparator();
        _propertiesContainer.AddChild(sep);
        _propertiesContainer.AddChild(header);
    }

    private void AddGroupHeader(string name)
    {
        var header = new Label();
        header.Text = $"  {name}";
        ExplorerTheme.StyleLabel(header, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeSmall);
        _propertiesContainer.AddChild(header);
    }

    private void AddPropertyRow(Node node, PropertyHelper.PropertyEntry prop)
    {
        var row = new HBoxContainer();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddThemeConstantOverride("separation", 4);

        // Property name label (40%)
        var nameLabel = new Label();
        nameLabel.Text = prop.Name;
        nameLabel.TooltipText = $"{prop.Name} ({PropertyHelper.TypeName(prop.VariantType)})";
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.SizeFlagsStretchRatio = 0.4f;
        nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        ExplorerTheme.StyleLabel(nameLabel, prop.IsReadOnly ? ExplorerTheme.TextDim : ExplorerTheme.TextColor,
            ExplorerTheme.FontSizeSmall);
        row.AddChild(nameLabel);

        // Value editor (60%)
        Variant currentValue = PropertyHelper.ReadValue(node, prop.Name);
        var editor = PropertyEditors.Create(
            prop.VariantType,
            prop.Hint,
            prop.HintString,
            currentValue,
            prop.IsReadOnly,
            (newValue) => OnPropertyChanged(prop.Name, newValue)
        );
        editor.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        editor.SizeFlagsStretchRatio = 0.6f;
        row.AddChild(editor);

        _propertiesContainer.AddChild(row);
        _propertyRows.Add(new PropertyRow(prop.Name, editor, prop.VariantType));
    }

    private void OnPropertyChanged(string propertyName, Variant newValue)
    {
        var obj = GodotObject.InstanceFromId(_inspectedNodeId);
        if (obj is Node node && GodotObject.IsInstanceValid(node))
        {
            if (!PropertyHelper.WriteValue(node, propertyName, newValue))
            {
                GD.PrintErr($"[GodotExplorer] Failed to set property: {propertyName}");
            }
        }
    }

    private void OnProcess()
    {
        if (!ExplorerCore.IsVisible) return;
        if (_inspectedNodeId == 0) return;

        // Check if inspected node still exists
        var obj = GodotObject.InstanceFromId(_inspectedNodeId);
        if (obj is not Node node || !GodotObject.IsInstanceValid(node))
        {
            ClearInspector("Node has been freed.");
            _inspectedNodeId = 0;
            return;
        }

        // Auto-refresh property values periodically
        _refreshTimer += Root.GetProcessDeltaTime();
        if (_refreshTimer >= RefreshInterval)
        {
            _refreshTimer = 0;
            RefreshValues(node);
        }
    }

    private void RefreshValues(Node node)
    {
        foreach (var row in _propertyRows)
        {
            // Skip refresh if the editor has focus (user is editing)
            if (EditorHasFocus(row.Editor)) continue;

            Variant value = PropertyHelper.ReadValue(node, row.Name);
            UpdateEditorValue(row.Editor, row.Type, value);
        }
    }

    private static bool EditorHasFocus(Control editor)
    {
        if (editor is LineEdit le) return le.HasFocus();
        if (editor is SpinBox sb) return sb.GetLineEdit()?.HasFocus() ?? false;
        if (editor is HBoxContainer hbox)
        {
            foreach (var child in hbox.GetChildren())
            {
                if (child is SpinBox csb && (csb.GetLineEdit()?.HasFocus() ?? false))
                    return true;
            }
        }
        return false;
    }

    private static void UpdateEditorValue(Control editor, Variant.Type type, Variant value)
    {
        try
        {
            switch (editor)
            {
                case CheckBox cb when type == Variant.Type.Bool:
                    cb.SetPressedNoSignal(value.AsBool());
                    break;
                case SpinBox sb when type == Variant.Type.Int || type == Variant.Type.Float:
                    sb.SetValueNoSignal(value.AsDouble());
                    break;
                case ColorPickerButton cpb when type == Variant.Type.Color:
                    cpb.Color = value.AsColor();
                    break;
                // Vector2 editors are HBoxContainers with SpinBoxes - skip for simplicity
                // (they update on next explicit refresh click)
            }
        }
        catch
        {
            // Value type mismatch or disposed — ignore silently
        }
    }

    private void ClearInspector(string message)
    {
        _headerLabel.Text = "Inspector";
        _typeLabel.Text = "";
        _pathLabel.Text = "";
        ClearProperties();

        var label = new Label();
        label.Text = message;
        ExplorerTheme.StyleLabel(label, ExplorerTheme.TextDim);
        _propertiesContainer.AddChild(label);
    }

    private void ClearProperties()
    {
        _propertyRows.Clear();
        foreach (var child in _propertiesContainer.GetChildren())
        {
            if (child is Node n)
                n.QueueFree();
        }
    }
}
