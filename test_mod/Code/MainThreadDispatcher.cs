using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace MCPTest;

/// <summary>
/// Captures the game's SynchronizationContext at init time and uses it to
/// dispatch work to the main thread. This is the proper .NET pattern for
/// cross-thread game state access in Godot/C#.
/// </summary>
public static class MainThreadDispatcher
{
    private static SynchronizationContext? _gameContext;

    /// <summary>
    /// Call this from the mod initializer (which runs on the main thread).
    /// </summary>
    public static void Capture()
    {
        _gameContext = SynchronizationContext.Current;
        if (_gameContext == null)
        {
            ModEntry.WriteLog("WARNING: SynchronizationContext.Current is null! Falling back to direct execution.");
        }
        else
        {
            ModEntry.WriteLog($"Captured SynchronizationContext: {_gameContext.GetType().Name}");
        }
    }

    /// <summary>
    /// Run an action on the main thread (fire and forget).
    /// </summary>
    public static void Post(Action action)
    {
        if (_gameContext != null)
        {
            _gameContext.Post(_ =>
            {
                try { action(); }
                catch (Exception ex) { ModEntry.WriteLog($"MainThread Post error: {ex.Message}"); }
            }, null);
        }
        else
        {
            // Fallback: run inline (risky but better than nothing)
            try { action(); }
            catch (Exception ex) { ModEntry.WriteLog($"Direct exec error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Run a function on the main thread and return its result. Blocks the calling thread.
    /// </summary>
    public static T Invoke<T>(Func<T> func)
    {
        if (_gameContext == null)
        {
            return func();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameContext.Post(_ =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);

        // Block until main thread completes (with timeout)
        if (!tcs.Task.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("Main thread dispatch timed out after 10s");
        }
        return tcs.Task.Result;
    }

    /// <summary>
    /// Run an action on the main thread and wait for completion.
    /// </summary>
    public static void Invoke(Action action)
    {
        Invoke<bool>(() => { action(); return true; });
    }
}
