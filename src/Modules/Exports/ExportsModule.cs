using ShoujoKageki.Core.Runtime;

namespace ShoujoKageki.Core.Exports;

public sealed class ExportsModule : ICoreModule
{
    internal static CoreContext Context { get; private set; } = null!;
    public string Id => "exports";
    public int Order => 200;
    public void Initialize(CoreContext context) { Context = context; context.Services.Add(this); }
}
