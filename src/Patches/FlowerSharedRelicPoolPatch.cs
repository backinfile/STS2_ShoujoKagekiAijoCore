using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using ShoujoKageki.Core.Runtime.Modules.Relics;

namespace ShoujoKageki.Core.Runtime.Patches;

[HarmonyPatch(typeof(SharedRelicPool), "GenerateAllRelics")]
public static class FlowerSharedRelicPoolPatch
{
    public static void Postfix(ref IEnumerable<RelicModel> __result)
    {
        var relics = __result.ToList();
        var flower = ModelDb.Relic<KarenFlowerRelic>();
        if (relics.All(relic => relic.Id != flower.Id)) relics.Add(flower);
        __result = relics;
    }
}
