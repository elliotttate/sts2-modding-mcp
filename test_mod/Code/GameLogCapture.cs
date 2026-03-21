using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;

namespace MCPTest;

/// <summary>
/// Hooks the game's Log.LogCallback to capture game log messages into a ring buffer.
/// This gives visibility into the game's own logging (Actions, Network, GameSync, etc.)
/// rather than just the bridge mod's log file.
/// </summary>
public static class GameLogCapture
{
    private static readonly object Lock = new();
    private static readonly LinkedList<LogEntry> Buffer = new();
    private const int MaxEntries = 500;
    private static int _nextId = 1;
    private static volatile bool _initialized;

    /// <summary>Minimum level to capture (inclusive). Default: Info.</summary>
    private static volatile LogLevel _minCaptureLevel = LogLevel.Info;
    public static LogLevel MinCaptureLevel
    {
        get => _minCaptureLevel;
        set => _minCaptureLevel = value;
    }

    public sealed class LogEntry
    {
        public int Id { get; init; }
        public DateTime Timestamp { get; init; }
        public string Level { get; init; } = "";
        public string Message { get; init; } = "";
    }

    public static void Initialize()
    {
        if (_initialized) return;
        lock (Lock)
        {
            if (_initialized) return;
            _initialized = true;

            Log.LogCallback += OnLogMessage;
            ModEntry.WriteLog("GameLogCapture: Hooked Log.LogCallback");
        }
    }

    private static void OnLogMessage(LogLevel level, string message, int skipFrames)
    {
        if (level < _minCaptureLevel) return;

        lock (Lock)
        {
            Buffer.AddLast(new LogEntry
            {
                Id = _nextId++,
                Timestamp = DateTime.Now,
                Level = level.ToString(),
                Message = message,
            });
            while (Buffer.Count > MaxEntries)
                Buffer.RemoveFirst();
        }
    }

    public static List<LogEntry> GetRecent(int maxCount = 100, int sinceId = 0, string? levelFilter = null, string? contains = null)
    {
        lock (Lock)
        {
            IEnumerable<LogEntry> query = Buffer.Where(e => e.Id > sinceId);

            if (!string.IsNullOrEmpty(levelFilter))
                query = query.Where(e => e.Level.Equals(levelFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(contains))
                query = query.Where(e => e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase));

            return query.TakeLast(maxCount).ToList();
        }
    }

    public static int LatestId
    {
        get { lock (Lock) { return Buffer.Last?.Value.Id ?? 0; } }
    }

    public static void Clear()
    {
        lock (Lock) { Buffer.Clear(); }
    }

    public static void SetMinLevel(LogLevel level)
    {
        MinCaptureLevel = level;
        ModEntry.WriteLog($"GameLogCapture: Min capture level set to {level}");
    }
}
