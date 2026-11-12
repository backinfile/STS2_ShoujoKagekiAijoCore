using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime;

public interface ICoreModule
{
    string Id { get; }
    int Order { get; }
    void Initialize(CoreContext context);
}
public sealed class CoreServices : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();
    public void Add<T>(T service) where T : class
    {
        if (!_services.TryAdd(typeof(T), service)) throw new InvalidOperationException($"Duplicate service {typeof(T)}.");
    }
    public object? GetService(Type type) => _services.GetValueOrDefault(type);
    public T Get<T>() where T : class => (T)(GetService(typeof(T)) ?? throw new InvalidOperationException($"Missing service {typeof(T)}."));
}
public sealed class CoreContext(CoreServices services, string releaseVersion, string gameVersion, string dataDirectory)
{
    public CoreServices Services { get; } = services;
    public string ReleaseVersion { get; } = releaseVersion;
    public string GameVersion { get; } = gameVersion;
    public string DataDirectory { get; } = dataDirectory;
    public string ResourceRoot => CoreApi.ResourceRoot;
    public ICoreConfiguration Configuration => Services.Get<ICoreConfiguration>();
    public ICoreDiagnostics Diagnostics => Services.Get<ICoreDiagnostics>();
    public ICoreDiscovery Discovery => Services.Get<ICoreDiscovery>();
    public event Action? ModelsReady;
    public event Action? ShuttingDown;
    internal void NotifyModelsReady() => ModelsReady?.Invoke();
    internal void NotifyShutdown() => Cleanup.Run(
        ShuttingDown?.GetInvocationList().Cast<Action>() ?? []);
}
