using Robust.Shared.Timing;

namespace Content.Server._CS.AICore;

/// <summary>
///     Rate-limits AI core responses per core ID.
///     Rules: max 1 response per second, max 10 responses per 30-second sliding window.
///     This prevents the AI from spamming chat in response to rapid-fire messages.
/// </summary>
public sealed class CoyoteRateLimiterSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<string, ResponseRecord> _records = new();

    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WindowSize = TimeSpan.FromSeconds(30);
    private const int MaxPerWindow = 10;

    public bool CanRespond(string coreId)
    {
        if (!_records.TryGetValue(coreId, out var record))
            return true;

        var now = _timing.CurTime;

        if (now - record.LastResponse < MinInterval)
            return false;

        record.Window.RemoveAll(t => now - t > WindowSize);

        if (record.Window.Count >= MaxPerWindow)
            return false;

        return true;
    }

    public void RegisterResponse(string coreId)
    {
        var now = _timing.CurTime;

        if (!_records.TryGetValue(coreId, out var record))
        {
            record = new ResponseRecord();
            _records[coreId] = record;
        }

        record.LastResponse = now;
        record.Window.Add(now);
        record.Window.RemoveAll(t => now - t > WindowSize);
    }

    public void Reset(string coreId)
    {
        _records.Remove(coreId);
    }

    private sealed class ResponseRecord
    {
        public TimeSpan LastResponse;
        public List<TimeSpan> Window = new();
    }
}
