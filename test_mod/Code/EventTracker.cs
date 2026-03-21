using System;
using System.Collections.Generic;
using System.Linq;

namespace MCPTest;

/// <summary>
/// Tracks game events in a ring buffer for polling via the bridge.
/// </summary>
public static class EventTracker
{
    private static readonly object Lock = new();
    private static readonly LinkedList<GameEvent> Buffer = new();
    private const int MaxEntries = 500;
    private static int _nextId = 1;

    public sealed class GameEvent
    {
        public int Id { get; init; }
        public DateTime Timestamp { get; init; }
        public string Type { get; init; } = "";
        public string Detail { get; init; } = "";
        public Dictionary<string, object?>? Data { get; init; }
    }

    public static void Record(string type, string detail, Dictionary<string, object?>? data = null)
    {
        lock (Lock)
        {
            Buffer.AddLast(new GameEvent
            {
                Id = _nextId++,
                Timestamp = DateTime.Now,
                Type = type,
                Detail = detail,
                Data = data,
            });
            while (Buffer.Count > MaxEntries)
                Buffer.RemoveFirst();
        }
    }

    public static List<GameEvent> GetSince(int sinceId = 0, int maxCount = 100)
    {
        lock (Lock)
        {
            return Buffer
                .Where(e => e.Id > sinceId)
                .TakeLast(maxCount)
                .ToList();
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
}
