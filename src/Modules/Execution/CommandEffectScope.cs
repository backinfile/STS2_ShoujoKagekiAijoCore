using System.Reflection;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>Follow-up effects belong to the task that invoked a synchronous native command.</summary>
internal sealed class CommandEffectScope : IDisposable
{
    private static readonly AsyncLocal<CommandEffectScope?> Current = new();
    private static readonly MethodInfo CompleteResult = typeof(CommandEffectScope).GetMethod(nameof(CompleteGeneric), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly CommandEffectScope? _previous = Current.Value;
    private readonly Queue<Task> _effects = new();
    private bool _disposed;
    internal static bool IsActive => Current.Value != null;
    internal CommandEffectScope() => Current.Value = this;
    internal static void Enqueue(Func<Task> effect)
    {
        var scope = Current.Value ?? throw new InvalidOperationException("Upgrade effects require an asynchronous model/action or CardUpgrades.Upgrade.");
        try { scope._effects.Enqueue(effect()); }
        catch (Exception error) { scope._effects.Enqueue(Task.FromException(error)); }
    }
    private async Task Drain()
    {
        var previous = Current.Value;
        Current.Value = this;
        var failures = new List<Exception>();
        try
        {
            while (_effects.TryDequeue(out var effect))
                try { await effect; }
                catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Synchronous command follow-up effects failed.", failures);
        }
        finally { Current.Value = previous; }
    }
    internal Task Complete(Task task)
    {
        for (var type = task.GetType(); type != null; type = type.BaseType)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                return (Task)CompleteResult.MakeGenericMethod(type.GenericTypeArguments).Invoke(this, [task])!;
        return CompleteVoid(task);
    }
    private async Task CompleteVoid(Task task)
    {
        try { await task; }
        finally { await Drain(); }
    }
    private async Task<T> CompleteGeneric<T>(Task<T> task)
    {
        try { return await task; }
        finally { await Drain(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Current.Value = _previous;
    }
}
