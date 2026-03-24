using Godot;
using GodotExplorer.UI;
using GodotExplorer.MCP;
using GodotExplorer.Patches;

namespace GodotExplorer.Core;

/// <summary>
/// Singleton coordinator. Manages lifecycle, UI, selected node state, MCP server, and subsystems.
/// </summary>
public static class ExplorerCore
{
    public const string Version = "1.0.0";

    private static SceneTree? _sceneTree;
    private static bool _initialized;
    private static MCPServer? _mcpServer;

    public static SceneTree SceneTree => _sceneTree!;
    public static ExplorerUI? UI { get; private set; }
    public static MouseInspect? MouseInspect { get; private set; }
    public static bool IsVisible { get; private set; }

    // Currently selected node (stored as instance ID for safety)
    private static ulong _selectedNodeId;

    public static Node? SelectedNode
    {
        get
        {
            if (_selectedNodeId == 0) return null;
            var obj = GodotObject.InstanceFromId(_selectedNodeId);
            if (obj is Node node && GodotObject.IsInstanceValid(node))
                return node;
            _selectedNodeId = 0;
            return null;
        }
    }

    // Events
    public static event System.Action<Node?>? NodeSelected;
    public static event System.Action<bool>? VisibilityChanged;

    public static void Initialize(SceneTree sceneTree)
    {
        if (_initialized) return;
        _initialized = true;
        _sceneTree = sceneTree;

        GD.Print($"[GodotExplorer] Initializing v{Version}...");

        // Install per-frame input polling and main thread dispatcher
        InputPatch.Install(sceneTree);
        sceneTree.Connect("process_frame", Callable.From(MainThreadDispatcher.ProcessQueue));

        // Start MCP server for direct Claude Code integration
        _mcpServer = new MCPServer(27020);
        _mcpServer.Start();

        // Create the mouse inspector
        MouseInspect = new MouseInspect(sceneTree);
        MouseInspect.NodePicked += (node) => SelectNode(node);

        // Create the UI
        UI = new ExplorerUI();
        sceneTree.Root.CallDeferred("add_child", UI.RootLayer);

        // Start hidden
        SetVisible(false);

        GD.Print("[GodotExplorer] Initialized. Press F12 to toggle.");
    }

    public static void ToggleExplorer()
    {
        SetVisible(!IsVisible);
    }

    public static void SetVisible(bool visible)
    {
        IsVisible = visible;
        if (UI != null)
        {
            UI.RootLayer.Visible = visible;
        }
        VisibilityChanged?.Invoke(visible);
    }

    public static void SelectNode(Node? node)
    {
        _selectedNodeId = node != null && GodotObject.IsInstanceValid(node)
            ? node.GetInstanceId()
            : 0;
        NodeSelected?.Invoke(node);
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        _mcpServer?.Stop();

        if (UI != null && GodotObject.IsInstanceValid(UI.RootLayer))
        {
            UI.RootLayer.QueueFree();
        }

        _initialized = false;
        GD.Print("[GodotExplorer] Shut down.");
    }
}
