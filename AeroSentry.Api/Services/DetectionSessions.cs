using System.Collections.Concurrent;

namespace AeroSentry.Api.Services;

public class DetectionSession
{
    public string MlSessionId { get; init; } = string.Empty;
    public double Threshold { get; init; }

    // The ML session keeps per-camera tracking state, so frames of one wall must go in one at a time.
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

// Which ML session belongs to which wall. One instance for the whole app (singleton), held in memory:
// like the ML service's own sessions, it is empty after a restart (DetectionService re-opens on demand).
public class DetectionSessions
{
    private readonly ConcurrentDictionary<Guid, DetectionSession> _byWall = new();

    public DetectionSession? Get(Guid wallId) => _byWall.GetValueOrDefault(wallId);

    public void Set(Guid wallId, DetectionSession session) => _byWall[wallId] = session;

    public DetectionSession? Remove(Guid wallId) => _byWall.TryRemove(wallId, out var s) ? s : null;
}
