using Godot;
using GodotExplorer.Core;
using System.Collections.Generic;

namespace GodotExplorer.UI;

/// <summary>
/// Scene hierarchy browser using Godot's Tree control.
/// Builds eagerly to a configurable depth, with lazy loading beyond that.
/// Auto-rebuilds after a delay to catch dynamically-added nodes.
/// </summary>
public class SceneTreePanel
{
    public VBoxContainer Root { get; }

    private LineEdit _filterInput;
    private Tree _tree;
    private Label _nodeCountLabel;
    private SceneTreeWatcher? _watcher;

    // Track expanded items by node path for state preservation across rebuilds
    private readonly HashSet<string> _expandedPaths = new();

    // Sentinel child used to show the expand arrow before children are loaded
    private const string LazyPlaceholder = "__lazy_placeholder__";

    // How many levels deep to build eagerly on initial load
    private const int EagerDepth = 3;

    // Auto-rebuild tracking
    private int _rebuildCount;
    private double _timeSinceLastRebuild;

    public SceneTreePanel()
    {
        Root = new VBoxContainer();
        Root.Name = "SceneTreePanel";
        Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Root.AddThemeConstantOverride("separation", ExplorerTheme.ItemSpacing);

        // Header
        var header = new Label();
        header.Text = "Scene Tree";
        ExplorerTheme.StyleLabel(header, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeHeader);
        Root.AddChild(header);

        // Filter input
        _filterInput = new LineEdit();
        _filterInput.PlaceholderText = "Filter nodes...";
        _filterInput.ClearButtonEnabled = true;
        ExplorerTheme.StyleLineEdit(_filterInput);
        _filterInput.TextChanged += OnFilterChanged;
        Root.AddChild(_filterInput);

        // Refresh + node count row
        var infoRow = new HBoxContainer();
        infoRow.AddThemeConstantOverride("separation", 4);
        Root.AddChild(infoRow);

        var refreshBtn = new Button();
        refreshBtn.Text = "Refresh";
        refreshBtn.TooltipText = "Rebuild tree from scene";
        ExplorerTheme.StyleButton(refreshBtn);
        refreshBtn.Pressed += () => RebuildTree();
        infoRow.AddChild(refreshBtn);

        _nodeCountLabel = new Label();
        ExplorerTheme.StyleLabel(_nodeCountLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        infoRow.AddChild(_nodeCountLabel);

        // Tree control
        _tree = new Tree();
        _tree.Name = "SceneTree";
        _tree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _tree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _tree.Columns = 2;
        _tree.SetColumnExpand(0, true);
        _tree.SetColumnExpand(1, false);
        _tree.SetColumnCustomMinimumWidth(1, 32);
        _tree.HideRoot = false;
        _tree.AllowRmbSelect = true;
        ExplorerTheme.StyleTree(_tree);

        _tree.ItemSelected += OnItemSelected;
        _tree.ItemActivated += OnItemActivated;
        _tree.CellSelected += OnCellSelected;
        _tree.ItemCollapsed += OnItemCollapsed;
        Root.AddChild(_tree);

        // Set up watcher and schedule builds
        SetupWatcher();
    }

    private void SetupWatcher()
    {
        Root.Ready += () =>
        {
            var sceneTree = ExplorerCore.SceneTree;
            if (sceneTree == null) return;

            _watcher = new SceneTreeWatcher(sceneTree);
            _watcher.TreeChanged += RebuildTree;

            // Build immediately with what's available
            RebuildTree();

            // Schedule auto-rebuilds to catch dynamically loaded nodes.
            // Game nodes like NGame add children in _Ready() which runs
            // after our init. We rebuild a few times in the first seconds.
            sceneTree.Connect("process_frame", Callable.From(OnProcessFrame));
        };
    }

    private void OnProcessFrame()
    {
        _timeSinceLastRebuild += ExplorerCore.SceneTree.Root.GetProcessDeltaTime();

        // Auto-rebuild at 1s, 3s, and 6s after startup to catch late-loading nodes
        if (_rebuildCount < 3)
        {
            double[] rebuildTimes = { 1.0, 3.0, 6.0 };
            if (_timeSinceLastRebuild >= rebuildTimes[_rebuildCount])
            {
                _rebuildCount++;
                RebuildTree();
            }
        }
    }

    public void RebuildTree()
    {
        // Pause watcher to prevent re-entry during tree manipulation
        if (_watcher != null) _watcher.Paused = true;

        try
        {
            _tree.Clear();

            var sceneTree = ExplorerCore.SceneTree;
            if (sceneTree?.Root == null) return;

            var root = _tree.CreateItem();
            root.SetText(0, sceneTree.Root.Name);
            root.SetMetadata(0, sceneTree.Root.GetPath().ToString());
            SetNodeTypeTag(root, sceneTree.Root);

            // Build children eagerly to EagerDepth levels
            BuildChildrenRecursive(root, sceneTree.Root, 0);

            // Count actual scene nodes (not tree items)
            int sceneNodeCount = CountSceneNodes(sceneTree.Root);
            int treeNodeCount = CountTreeItems(root);
            _nodeCountLabel.Text = $"{treeNodeCount} shown / {sceneNodeCount} total";
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[GodotExplorer] RebuildTree error: {ex.Message}");
        }
        finally
        {
            if (_watcher != null) _watcher.Paused = false;
        }
    }

    private void BuildChildrenRecursive(TreeItem parentItem, Node parentNode, int depth)
    {
        int childCount;
        try { childCount = parentNode.GetChildCount(); }
        catch { return; }

        for (int i = 0; i < childCount; i++)
        {
            Node child;
            try { child = parentNode.GetChild(i); }
            catch { continue; }

            if (!GodotObject.IsInstanceValid(child)) continue;
            string childName = child.Name.ToString();
            if (childName.StartsWith("GodotExplorer")) continue;

            var item = _tree.CreateItem(parentItem);
            PopulateTreeItem(item, child);

            int grandChildCount;
            try { grandChildCount = child.GetChildCount(); }
            catch { grandChildCount = 0; }

            if (grandChildCount > 0)
            {
                if (depth < EagerDepth)
                {
                    // Build children eagerly
                    BuildChildrenRecursive(item, child, depth + 1);

                    // Auto-expand first 2 levels and key nodes, or previously expanded
                    bool wasExpanded = _expandedPaths.Contains(child.GetPath().ToString());
                    bool autoExpand = depth < 2
                        || childName == "Game"
                        || childName == "RootSceneContainer"
                        || childName == "GlobalUi"
                        || childName == "Run";
                    item.Collapsed = !(wasExpanded || autoExpand);
                }
                else
                {
                    // Beyond eager depth: add lazy placeholder
                    var placeholder = _tree.CreateItem(item);
                    placeholder.SetText(0, $"... ({grandChildCount} children)");
                    placeholder.SetMetadata(0, LazyPlaceholder);
                    item.Collapsed = !_expandedPaths.Contains(child.GetPath().ToString());
                }
            }
        }
    }

    private void PopulateTreeItem(TreeItem item, Node node)
    {
        string typeName = node.GetClass();
        int childCount;
        try { childCount = node.GetChildCount(); }
        catch { childCount = 0; }

        // Show child count for nodes that have children
        string label = childCount > 0
            ? $"{node.Name}  [{typeName}] ({childCount})"
            : $"{node.Name}  [{typeName}]";
        item.SetText(0, label);
        item.SetMetadata(0, node.GetPath().ToString());
        SetNodeTypeTag(item, node);

        // Visibility toggle for CanvasItem nodes
        if (node.HasMethod("is_visible"))
        {
            try
            {
                bool visible = node.Call("is_visible").AsBool();
                item.SetText(1, visible ? "[V]" : "[H]");
                item.SetTooltipText(1, "Click to toggle visibility");
                item.SetSelectable(1, true);
            }
            catch { /* node may have been freed */ }
        }
    }

    private void SetNodeTypeTag(TreeItem item, Node node)
    {
        // Color-code by type for quick visual scanning
        Color color = ExplorerTheme.TextColor;
        string className = node.GetClass();

        if (className == "Control" || node is Control) color = new Color(0.9f, 0.8f, 0.5f);
        else if (className == "CanvasLayer" || node is CanvasLayer) color = new Color(0.95f, 0.65f, 0.4f);
        else if (className == "Node2D" || node is Node2D) color = new Color(0.6f, 0.8f, 1.0f);
        else if (className == "CanvasItem" || node is CanvasItem) color = new Color(0.6f, 0.9f, 0.6f);

        item.SetCustomColor(0, color);
    }

    private void OnItemCollapsed(TreeItem item)
    {
        string path = item.GetMetadata(0).AsString();

        if (item.Collapsed)
        {
            // Track collapse
            _expandedPaths.Remove(path);
            return;
        }

        // Expanding
        _expandedPaths.Add(path);

        // Check if this has a lazy placeholder as first child
        var firstChild = item.GetFirstChild();
        if (firstChild != null && firstChild.GetMetadata(0).AsString() == LazyPlaceholder)
        {
            // Remove placeholder
            firstChild.Free();

            // Pause watcher during tree modifications
            if (_watcher != null) _watcher.Paused = true;
            try
            {
                var sceneTree = ExplorerCore.SceneTree;
                if (sceneTree?.Root == null) return;

                var node = sceneTree.Root.GetNodeOrNull(path);
                if (node != null)
                {
                    // Build children eagerly for expanded nodes (next 3 levels)
                    BuildChildrenRecursive(item, node, EagerDepth - 1);
                }
            }
            finally
            {
                if (_watcher != null) _watcher.Paused = false;
            }
        }
    }

    private void OnItemSelected()
    {
        var selected = _tree.GetSelected();
        if (selected == null) return;

        int col = _tree.GetSelectedColumn();

        string path = selected.GetMetadata(0).AsString();
        if (path == LazyPlaceholder) return;

        // Select node in the inspector
        var sceneTree = ExplorerCore.SceneTree;
        var node = sceneTree?.Root?.GetNodeOrNull(path);
        ExplorerCore.SelectNode(node);

        // Auto-expand when clicking on column 0 if the item has children and is collapsed.
        // This makes the whole row clickable for expansion, not just the tiny arrow.
        if (col == 0 && selected.Collapsed && selected.GetFirstChild() != null)
        {
            selected.Collapsed = false;
            // The ItemCollapsed signal will fire and handle lazy loading
        }
    }

    private void OnItemActivated()
    {
        // Double-click toggles expand/collapse
        var selected = _tree.GetSelected();
        if (selected == null) return;

        if (selected.GetFirstChild() != null)
        {
            selected.Collapsed = !selected.Collapsed;
        }
    }

    private void OnCellSelected()
    {
        var selected = _tree.GetSelected();
        if (selected == null) return;

        int col = _tree.GetSelectedColumn();
        if (col != 1) return;

        string path = selected.GetMetadata(0).AsString();
        if (path == LazyPlaceholder) return;

        var sceneTree = ExplorerCore.SceneTree;
        var node = sceneTree?.Root?.GetNodeOrNull(path);

        if (node != null && node.HasMethod("is_visible") && node.HasMethod("set_visible"))
        {
            try
            {
                bool currentlyVisible = node.Call("is_visible").AsBool();
                node.Call("set_visible", !currentlyVisible);
                selected.SetText(1, currentlyVisible ? "[H]" : "[V]");
            }
            catch { /* node may have been freed */ }
        }
    }

    private void OnFilterChanged(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            SetAllVisible(_tree.GetRoot());
            return;
        }

        string filter = text.ToLowerInvariant();
        FilterTreeItem(_tree.GetRoot(), filter);
    }

    private bool FilterTreeItem(TreeItem? item, string filter)
    {
        if (item == null) return false;

        bool anyChildVisible = false;
        var child = item.GetFirstChild();
        while (child != null)
        {
            if (FilterTreeItem(child, filter))
                anyChildVisible = true;
            child = child.GetNext();
        }

        string text = item.GetText(0).ToLowerInvariant();
        bool matches = text.Contains(filter) || anyChildVisible;
        item.Visible = matches;

        if (matches && anyChildVisible)
            item.Collapsed = false;

        return matches;
    }

    private void SetAllVisible(TreeItem? item)
    {
        if (item == null) return;
        item.Visible = true;
        var child = item.GetFirstChild();
        while (child != null)
        {
            SetAllVisible(child);
            child = child.GetNext();
        }
    }

    private int CountTreeItems(TreeItem? item)
    {
        if (item == null) return 0;
        int count = 1;
        var child = item.GetFirstChild();
        while (child != null)
        {
            if (child.GetMetadata(0).AsString() != LazyPlaceholder)
                count += CountTreeItems(child);
            child = child.GetNext();
        }
        return count;
    }

    /// <summary>Count actual nodes in the scene (not tree items).</summary>
    private int CountSceneNodes(Node node)
    {
        int count = 1;
        int childCount;
        try { childCount = node.GetChildCount(); }
        catch { return count; }

        for (int i = 0; i < childCount; i++)
        {
            try
            {
                var child = node.GetChild(i);
                if (child.Name.ToString().StartsWith("GodotExplorer")) continue;
                count += CountSceneNodes(child);
            }
            catch { }
        }
        return count;
    }
}
