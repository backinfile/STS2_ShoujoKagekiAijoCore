using ShoujoKageki.Core.Runtime.Modules.Execution;

int checks = 0;
void Check(bool passed, string label)
{
    if (!passed) throw new InvalidOperationException(label);
    checks++;
}
foreach (bool local in new[] { true, false })
foreach (int result in new[] { -1, 0, 2 })
{
    var events = new List<string>();
    int actual = await SynchronizedChoice.ChooseIndex(3, local,
        () => { events.Add("reserve"); return 41; },
        async () => { events.Add("local"); await Task.Yield(); return result; },
        (id, selected) => events.Add($"publish:{id}:{selected}"),
        async id => { events.Add($"receive:{id}"); await Task.Yield(); return result; });
    Check(actual == result, "Choice and cancellation roundtrip");
    Check(events.SequenceEqual(local
        ? new[] { "reserve", "local", $"publish:41:{result}" }
        : new[] { "reserve", "receive:41" }), "Use exactly one local or remote channel with the reserved ID");
}
foreach (bool local in new[] { true, false })
foreach (int result in new[] { -2, 3 })
{
    bool published = false, rejected = false;
    try
    {
        await SynchronizedChoice.ChooseIndex(3, local, () => 1, () => Task.FromResult(result),
            (_, _) => published = true, _ => Task.FromResult(result));
    }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected && !published, "Invalid indices are rejected before publication");
}
Check(await SynchronizedChoice.ChooseIndex(0, true,
    () => throw new Exception("No reservation expected"), () => throw new Exception("No presentation expected"),
    (_, _) => throw new Exception("No publication expected"), _ => throw new Exception("No receive expected")) == -1,
    "Empty choices have no channel or presentation side effects");
var expected = new InvalidOperationException("selection failure");
bool original = false;
try
{
    await SynchronizedChoice.ChooseIndex(2, true, () => 2, () => throw expected,
        (_, _) => throw new Exception("Failure must not publish a choice"), _ => Task.FromResult(0));
}
catch (Exception error) when (ReferenceEquals(error, expected)) { original = true; }
Check(original, "Local presentation preserves the original exception");
bool negative = false;
try
{
    await SynchronizedChoice.ChooseIndex(-1, true,
        () => throw new Exception("No reservation expected"), () => Task.FromResult(0), (_, _) => {}, _ => Task.FromResult(0));
}
catch (ArgumentOutOfRangeException) { negative = true; }
Check(negative, "Negative counts fail before any channel effects");
Console.WriteLine($"PASS {checks} custom choice checks.");
