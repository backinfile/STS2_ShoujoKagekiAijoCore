using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

public sealed class CardCloneModule : ICoreModule, ICardCloneEvents
{
    private static CardCloneModule _instance = null!;
    public string Id => "card-clone-events";
    public int Order => -300;
    public event Action<CardModel, CardModel>? Cloned;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<ICardCloneEvents>(this);
    }
    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
    private static class NativeClone
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(AbstractModel __instance, AbstractModel __result)
        {
            if (__instance is CardModel source && __result is CardModel copy && source.GetType() == copy.GetType())
                _instance.Cloned?.Invoke(source, copy);
        }
    }
}
