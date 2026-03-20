using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Godot;

namespace MCPTest;

/// <summary>
/// Uses Godot's CallDeferred to run actions on the main thread.
/// Since mod C# nodes may not get _Process calls, we use a polling timer instead.
/// </summary>
public static class MainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> _actionQueue = new();
    private static bool _initialized;

    public static void Initialize(SceneTree sceneTree)
    {
        if (_initialized) return;
        _initialized = true;

        // Create a timer that fires every frame to process the queue
        var timer = new Timer();
        timer.WaitTime = 0.016; // ~60fps
        timer.Autostart = true;
        timer.Timeout += ProcessQueue;
        sceneTree.Root.CallDeferred("add_child", timer);
        ModEntry.WriteLog("MainThreadDispatcher: timer-based dispatcher initialized.");
    }

    private static void ProcessQueue()
    {
        while (_actionQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ModEntry.WriteLog($"MainThread action error: {ex.Message}");
            }
        }
    }

    public static void Enqueue(Action action)
    {
        _actionQueue.Enqueue(action);
    }
}
