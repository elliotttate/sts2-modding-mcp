using Godot;
using System;

namespace GodotExplorer.Core;

/// <summary>
/// Monitors SceneTree signals (node_added, node_removed) and exposes
/// C# events with debouncing for efficient UI updates.
/// </summary>
public class SceneTreeWatcher
{
    private readonly SceneTree _sceneTree;
    private bool _fullRebuildPending;
    private int _pendingChanges;
    private const int RebuildThreshold = 200;

    /// <summary>Set to true to suppress all events (e.g., during our own tree operations).</summary>
    public bool Paused { get; set; }

    public event Action<Node>? NodeAdded;
    public event Action<Node>? NodeRemoved;
    public event Action? TreeChanged;

    public SceneTreeWatcher(SceneTree sceneTree)
    {
        _sceneTree = sceneTree;

        sceneTree.Connect("node_added", Callable.From<Node>(OnNodeAdded));
        sceneTree.Connect("node_removed", Callable.From<Node>(OnNodeRemoved));
        sceneTree.Connect("process_frame", Callable.From(OnProcessFrame));
    }

    private void OnNodeAdded(Node node)
    {
        if (Paused) return;

        // Ignore our own nodes
        if (node.Name.ToString().StartsWith("GodotExplorer")) return;

        _pendingChanges++;
        if (_pendingChanges > RebuildThreshold)
        {
            _fullRebuildPending = true;
        }
    }

    private void OnNodeRemoved(Node node)
    {
        if (Paused) return;

        // Ignore our own nodes
        if (node.Name.ToString().StartsWith("GodotExplorer")) return;

        _pendingChanges++;
        if (_pendingChanges > RebuildThreshold)
        {
            _fullRebuildPending = true;
        }
    }

    private void OnProcessFrame()
    {
        if (Paused) { _pendingChanges = 0; return; }

        if (_fullRebuildPending)
        {
            _fullRebuildPending = false;
            _pendingChanges = 0;
            try { TreeChanged?.Invoke(); }
            catch (Exception ex) { GD.PrintErr($"[GodotExplorer] TreeChanged handler error: {ex.Message}"); }
        }
        else
        {
            _pendingChanges = 0;
        }
    }

    public void Disconnect()
    {
        // Signal disconnection with Callable.From can be tricky — just set paused
        Paused = true;
    }
}
