using ShoujoKageki.Core.V1;
using MegaCrit.Sts2.Core.Logging;

namespace ShoujoKageki.Core.Runtime;

public sealed class CoreDiagnostics : ICoreDiagnostics
{
    private readonly object _gate = new();
    private readonly Queue<CoreDiagnostic> _records = new();
    private readonly Logger _logger = new(CoreApi.ModId, LogType.Generic);
    public event Action<CoreDiagnostic>? Reported;
    public IReadOnlyList<CoreDiagnostic> Snapshot() { lock (_gate) return Array.AsReadOnly(_records.ToArray()); }
    public void Report(DiagnosticSeverity severity, string source, string code, string message, Exception? error = null)
    {
        var record = new CoreDiagnostic(DateTimeOffset.UtcNow, severity, source, code, error == null ? message : message + "\n" + error);
        lock (_gate) { _records.Enqueue(record); while (_records.Count > 256) _records.Dequeue(); }
        var text = $"[{source}/{code}] {record.Message}";
        if (severity == DiagnosticSeverity.Error) _logger.Error(text);
        else if (severity == DiagnosticSeverity.Warning) _logger.Warn(text);
        else _logger.Info(text);
        foreach (var handler in Reported?.GetInvocationList() ?? [])
        {
            try { ((Action<CoreDiagnostic>)handler)(record); }
            catch (Exception observerError) { _logger.Warn($"Diagnostic observer failed: {observerError.Message}"); }
        }
    }
}
