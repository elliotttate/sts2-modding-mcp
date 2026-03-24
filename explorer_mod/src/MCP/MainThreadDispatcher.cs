using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GodotExplorer.MCP;

/// <summary>
/// Thread-safe work queue for dispatching actions from TCP background threads
/// to the Godot main thread. ProcessQueue() is called every frame.
/// </summary>
public static class MainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> _queue = new();

    /// <summary>Enqueue work from any thread.</summary>
    public static void Enqueue(Action action) => _queue.Enqueue(action);

    /// <summary>
    /// Execute an action on the main thread and wait for it to complete.
    /// Call this from background threads when you need a result from the main thread.
    /// </summary>
    public static T RunOnMainThread<T>(Func<T> func, int timeoutMs = 30000)
    {
        var result = default(T);
        Exception? exception = null;
        var done = new ManualResetEventSlim(false);

        _queue.Enqueue(() =>
        {
            try { result = func(); }
            catch (Exception ex) { exception = ex; }
            finally { done.Set(); }
        });

        if (!done.Wait(timeoutMs))
            throw new TimeoutException("Main thread operation timed out.");

        if (exception != null)
            throw new Exception($"Main thread error: {exception.Message}", exception);

        return result!;
    }

    /// <summary>Run on main thread, no return value.</summary>
    public static void RunOnMainThread(Action action, int timeoutMs = 30000)
    {
        RunOnMainThread<object?>(() => { action(); return null; }, timeoutMs);
    }

    /// <summary>Called every frame from the main thread (via process_frame signal).</summary>
    public static void ProcessQueue()
    {
        int processed = 0;
        while (_queue.TryDequeue(out var action) && processed < 50)
        {
            try { action(); }
            catch (Exception ex)
            {
                Godot.GD.PrintErr($"[GodotExplorer] MainThread dispatch error: {ex.Message}");
            }
            processed++;
        }
    }
}
