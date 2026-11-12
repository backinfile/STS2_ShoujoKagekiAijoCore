using System.Collections;
using System.Reflection;
using ShoujoKageki.Core.Runtime;
using ShoujoKageki.Core.V1;

var order = new List<int>();
var primary = new InvalidOperationException("notification");
var cleanup = new InvalidOperationException("module");
var diagnostics = new Diagnostics();
var services = new CoreServices();
services.Add<ICoreDiagnostics>(diagnostics);
var context = new CoreContext(services, "test", "test", "");
context.ShuttingDown += () => { order.Add(1); throw primary; };
context.ShuttingDown += () => order.Add(2);
typeof(RuntimeEntry).GetProperty(nameof(RuntimeEntry.Context))!.SetValue(null, context);
var modules = (IList)typeof(RuntimeEntry).GetField("Modules", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
modules.Add(new Module(() => order.Add(3)));
modules.Add(new Module(() => { order.Add(4); throw cleanup; }));
var shutdown = typeof(RuntimeEntry).GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic)!;
shutdown.Invoke(null, null);
if (!order.SequenceEqual(new[] { 1, 2, 4, 3 })) throw new InvalidOperationException("All notifications and reverse module cleanup must finish");
if (diagnostics.Error is not AggregateException failures || !failures.Flatten().InnerExceptions.SequenceEqual(new[] { primary, cleanup }))
    throw new InvalidOperationException("Shutdown diagnostics must retain both failures");
if (modules.Count != 0) throw new InvalidOperationException("Released modules must be detached");
shutdown.Invoke(null, null);
if (order.Count != 4 || diagnostics.Count != 1) throw new InvalidOperationException("Shutdown must run once");
Console.WriteLine("PASS 4 shutdown checks.");

sealed class Module(Action release) : ICoreModule, IDisposable
{
    public string Id => "test";
    public int Order => 0;
    public void Initialize(CoreContext context) { }
    public void Dispose() => release();
}
sealed class Diagnostics : ICoreDiagnostics
{
    internal Exception? Error;
    internal int Count;
    public event Action<CoreDiagnostic>? Reported { add { } remove { } }
    public IReadOnlyList<CoreDiagnostic> Snapshot() => [];
    public void Report(DiagnosticSeverity severity, string source, string code, string message, Exception? error = null)
    { Error = error; Count++; }
}
