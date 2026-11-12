using System.Runtime.ExceptionServices;

namespace ShoujoKageki.Core.Runtime;

internal static class Cleanup
{
    internal static Exception Combine(Exception? original, Exception cleanup)
        => original == null ? cleanup : new AggregateException("Operation and cleanup failed.", original, cleanup);

    internal static Exception? Capture(IEnumerable<Action> actions, Exception? error = null)
    {
        foreach (var action in actions)
            try { action(); }
            catch (Exception failure) { error = Combine(error, failure); }
        return error;
    }

    internal static void Run(IEnumerable<Action> actions, Exception? error = null)
    {
        if (Capture(actions, error) is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal static async Task Complete(Task operation, Func<ValueTask> cleanup)
    {
        Exception? error = null;
        try { await operation; }
        catch (Exception failure) { error = failure; }
        try { await cleanup(); }
        catch (Exception failure) { error = Combine(error, failure); }
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    internal static async Task<T> Complete<T>(Task<T> operation, Func<ValueTask> cleanup)
    {
        T result = default!;
        Exception? error = null;
        try { result = await operation; }
        catch (Exception failure) { error = failure; }
        try { await cleanup(); }
        catch (Exception failure) { error = Combine(error, failure); }
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }
}
