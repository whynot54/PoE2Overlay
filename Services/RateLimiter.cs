namespace PoE2Overlay.Services;

public sealed class RateLimiter
{
    private readonly object _lock = new();
    private readonly List<DateTime> _requestTimestamps = [];
    private int _maxRequests = 1;
    private TimeSpan _window = TimeSpan.FromSeconds(5);

    public void UpdateLimits(int maxRequests, int windowSeconds)
    {
        lock (_lock)
        {
            _maxRequests = maxRequests;
            _window = TimeSpan.FromSeconds(windowSeconds);
        }
    }

    public async Task WaitForSlotAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _requestTimestamps.RemoveAll(t => now - t > _window);

                if (_requestTimestamps.Count < _maxRequests)
                {
                    _requestTimestamps.Add(now);
                    return;
                }
            }

            await Task.Delay(500, ct);
        }
    }

    public void ParseHeaders(string? limitHeader, string? stateHeader)
    {
        if (string.IsNullOrEmpty(limitHeader))
            return;

        // Format: "5:10:60" means 5 requests per 10 seconds, penalty 60s
        var parts = limitHeader.Split(':');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var max)
            && int.TryParse(parts[1], out var window))
        {
            UpdateLimits(max, window);
        }
    }
}
