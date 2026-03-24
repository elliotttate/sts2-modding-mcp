using Godot;
using GodotExplorer.Core;

namespace GodotExplorer.UI;

/// <summary>
/// Search panel for finding nodes by name, type, or group.
/// </summary>
public class SearchPanel
{
    public VBoxContainer Root { get; }

    private LineEdit _searchInput;
    private Tree _resultsTree;
    private Label _statusLabel;

    private enum SearchMode { Name, Type, Group }
    private SearchMode _currentMode = SearchMode.Name;

    public SearchPanel()
    {
        Root = new VBoxContainer();
        Root.Name = "SearchPanel";
        Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Root.AddThemeConstantOverride("separation", ExplorerTheme.ItemSpacing);

        // Header
        var header = new Label();
        header.Text = "Search Nodes";
        ExplorerTheme.StyleLabel(header, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeHeader);
        Root.AddChild(header);

        // Search input
        _searchInput = new LineEdit();
        _searchInput.PlaceholderText = "Search...";
        _searchInput.ClearButtonEnabled = true;
        ExplorerTheme.StyleLineEdit(_searchInput);
        _searchInput.TextSubmitted += OnSearchSubmitted;
        Root.AddChild(_searchInput);

        // Mode buttons
        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 4);
        Root.AddChild(modeRow);

        AddModeButton(modeRow, "Name", SearchMode.Name, true);
        AddModeButton(modeRow, "Type", SearchMode.Type, false);
        AddModeButton(modeRow, "Group", SearchMode.Group, false);

        // Status label
        _statusLabel = new Label();
        ExplorerTheme.StyleLabel(_statusLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        Root.AddChild(_statusLabel);

        // Results tree
        _resultsTree = new Tree();
        _resultsTree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _resultsTree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _resultsTree.Columns = 1;
        _resultsTree.HideRoot = true;
        ExplorerTheme.StyleTree(_resultsTree);
        _resultsTree.ItemActivated += OnResultActivated;
        Root.AddChild(_resultsTree);
    }


    private void AddModeButton(HBoxContainer parent, string text, SearchMode mode, bool active)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.ButtonPressed = active;
        ExplorerTheme.StyleButton(btn);
        btn.Pressed += () =>
        {
            _currentMode = mode;
            // Depress other buttons
            foreach (var child in parent.GetChildren())
            {
                if (child is Button b && b != btn)
                    b.ButtonPressed = false;
            }
            btn.ButtonPressed = true;

            // Re-run search if there's text
            if (!string.IsNullOrEmpty(_searchInput.Text))
                DoSearch(_searchInput.Text);
        };
        parent.AddChild(btn);
    }

    private void OnSearchSubmitted(string query)
    {
        DoSearch(query);
    }

    private void DoSearch(string query)
    {
        _resultsTree.Clear();
        var root = _resultsTree.CreateItem();

        if (string.IsNullOrWhiteSpace(query))
        {
            _statusLabel.Text = "Enter a search query.";
            return;
        }

        var sceneTree = ExplorerCore.SceneTree;
        if (sceneTree?.Root == null) return;

        Godot.Collections.Array<Node> results;

        switch (_currentMode)
        {
            case SearchMode.Name:
                results = sceneTree.Root.FindChildren($"*{query}*", "", true, false);
                break;
            case SearchMode.Type:
                results = sceneTree.Root.FindChildren("*", query, true, false);
                break;
            case SearchMode.Group:
                results = sceneTree.GetNodesInGroup(query);
                break;
            default:
                return;
        }

        int count = 0;
        foreach (var node in results)
        {
            if (!GodotObject.IsInstanceValid(node)) continue;
            if (node.Name.ToString().StartsWith("GodotExplorer")) continue;

            var item = _resultsTree.CreateItem(root);
            item.SetText(0, $"{node.Name}  [{node.GetClass()}]");
            item.SetMetadata(0, node.GetPath().ToString());
            item.SetTooltipText(0, node.GetPath().ToString());

            // Color by type
            Color color = ExplorerTheme.TextColor;
            if (node is CanvasItem) color = new Color(0.6f, 0.9f, 0.6f);
            else if (node is Control) color = new Color(0.9f, 0.8f, 0.5f);
            item.SetCustomColor(0, color);

            count++;
            if (count >= 500) break; // Limit results
        }

        _statusLabel.Text = $"{count} result{(count == 1 ? "" : "s")} found.";
    }

    private void OnResultActivated()
    {
        var selected = _resultsTree.GetSelected();
        if (selected == null) return;

        string path = selected.GetMetadata(0).AsString();
        var sceneTree = ExplorerCore.SceneTree;
        var node = sceneTree?.Root?.GetNodeOrNull(path);

        if (node != null)
        {
            ExplorerCore.SelectNode(node);
        }
    }
}
