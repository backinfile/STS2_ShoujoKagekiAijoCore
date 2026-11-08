using MegaCrit.Sts2.Core.Nodes.Screens;
using ShoujoKageki.Core.V1.Presentation;
namespace ShoujoKageki.Core.Runtime.Modules.Presentation;
public sealed class PileViewEffectsModule : ICoreModule, IPileViewEffects
{
    public string Id => "pile-view-effects";
    public int Order => 0;
    public void Initialize(CoreContext context) => context.Services.Add<IPileViewEffects>(this);
    public void Attach(NCardPileScreen screen, bool promise) => NKarenPileViewVfx.Attach(screen, promise);
}
