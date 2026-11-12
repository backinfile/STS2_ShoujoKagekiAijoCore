namespace ShoujoKageki.Core.Telemetry;

/// <summary>One queue per endpoint; switching back resumes the same delivery worker.</summary>
internal sealed class TelemetryChannel(Func<Uri, TelemetryUploadQueue> create)
{
    private readonly Dictionary<Uri, TelemetryUploadQueue> _queues = [];
    private TelemetryUploadQueue? _active;

    internal TelemetryUploadQueue? Select(Uri? endpoint)
    {
        lock (_queues)
        {
            if (endpoint == _active?.Endpoint) return _active;
            _active?.Pause();
            if (endpoint == null) return _active = null;
            if (!_queues.TryGetValue(endpoint, out var queue))
                _queues.Add(endpoint, queue = create(endpoint));
            return _active = queue;
        }
    }

    internal void Pause()
    {
        lock (_queues)
            foreach (var queue in _queues.Values) queue.Pause();
    }
}
