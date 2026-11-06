namespace ShoujoKageki.Core.V1;

public static class CoreApi
{
    public const int ApiMajor = 1;
    public const string ModId = "ShoujoKagekiAijoCore";
    public const string ResourceRoot = "res://ShoujoKagekiAijoCore/";
    private static IServiceProvider? _services;
    public static bool IsReady => _services != null;
    public static T Get<T>() where T : class => (_services?.GetService(typeof(T)) as T)
        ?? throw new InvalidOperationException($"Core service {typeof(T).FullName} is unavailable.");
    public static ICoreConfiguration Configuration => Get<ICoreConfiguration>();
    public static void Bind(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (_services != null && !ReferenceEquals(_services, services))
            throw new InvalidOperationException("Core API has already been bound.");
        _services = services;
    }
}

public enum DiagnosticSeverity { Info, Warning, Error }
public sealed record CoreDiagnostic(DateTimeOffset Time, DiagnosticSeverity Severity, string Source, string Code, string Message);
public interface ICoreDiagnostics
{
    IReadOnlyList<CoreDiagnostic> Snapshot();
    void Report(DiagnosticSeverity severity, string source, string code, string message, Exception? error = null);
    event Action<CoreDiagnostic>? Reported;
}
public sealed record CoreModInfo(string Id, string Version, string Source, bool Loaded);
public sealed record CoreModelInfo(string Id, string Kind, string ModId, string ModVersion, Type ModelType);
public interface ICoreDiscovery
{
    IReadOnlyList<CoreModInfo> Mods { get; }
    IReadOnlyList<CoreModelInfo> Models { get; }
    event Action? ModelsChanged;
    void Refresh();
}
