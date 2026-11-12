using ShoujoKageki.Core.Runtime;
using ShoujoKageki.Core.Runtime.Modules.Execution;

int checks = 0;
void Check(bool passed, string name)
{
    if (!passed) throw new InvalidOperationException(name);
    checks++;
}
async Task<Exception?> Failure(Task task)
{
    try { await task; return null; }
    catch (Exception error) { return error; }
}
var original = new InvalidOperationException("operation");
var cleanup = new InvalidOperationException("cleanup");
int released = 0;
Check(ReferenceEquals(await Failure(Cleanup.Complete(Task.FromException(original), () =>
{ released++; return ValueTask.CompletedTask; })), original), "single failure retains identity");
Check(released == 1, "cleanup runs after failed operation");
var combined = await Failure(Cleanup.Complete(Task.FromException(original), () => ValueTask.FromException(cleanup)));
Check(combined is AggregateException aggregate && aggregate.InnerExceptions.SequenceEqual(new[] { original, cleanup }),
    "dual failures retain operation and cleanup in order");
Check(ReferenceEquals(await Failure(Cleanup.Complete(Task.CompletedTask, () => ValueTask.FromException(cleanup))), cleanup),
    "single cleanup failure retains identity");
Check(await Cleanup.Complete(Task.FromResult(42), () => ValueTask.CompletedTask) == 42, "generic completion preserves result");
Check(ReferenceEquals(await Failure(Cleanup.Complete(Task.FromException<int>(original), () => ValueTask.CompletedTask)), original),
    "generic completion preserves original failure");
var order = new List<int>();
var errors = Cleanup.Capture([() => { order.Add(1); throw original; }, () => order.Add(2), () => { order.Add(3); throw cleanup; }]);
Check(order.SequenceEqual(new[] { 1, 2, 3 }), "cleanup continues through every release");
Check(errors is AggregateException both && both.InnerExceptions.SequenceEqual(new[] { original, cleanup }), "all release failures retained");
using (var parent = new CommandEffectScope())
{
    Task completion;
    using (var child = new CommandEffectScope())
    {
        CommandEffectScope.Enqueue(() => throw cleanup);
        completion = child.Complete(Task.FromException(original));
    }
    var failure = await Failure(completion);
    Check(failure is AggregateException failed && failed.Flatten().InnerExceptions.SequenceEqual(new[] { original, cleanup }),
        "follow-up failure preserves operation error");
    Check(CommandEffectScope.IsActive, "nested scope restores parent after failure");
    CommandEffectScope.Enqueue(() => { released++; return Task.CompletedTask; });
    await parent.Complete(Task.CompletedTask);
}
Check(!CommandEffectScope.IsActive && released == 2, "parent drains and caller context is restored");
Console.WriteLine($"PASS {checks} lifecycle checks.");
