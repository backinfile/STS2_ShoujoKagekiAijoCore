#if STS2_BETA
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace ShoujoKageki.Core.Runtime.Patches;

// v0.111 Shuffle uses a silent Add, runs pile-change hooks, then animates the
// saved results. A hook can draw the shuffled cards before those animations.
// Use Add's normal visual-before-hook ordering for this one call site.
[HarmonyPatch]
internal static class ShuffleMoveOrderPatch
{
    private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(
        AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Shuffle)));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var add = AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add),
            [typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition),
             typeof(AbstractModel), typeof(bool), typeof(bool)]);
        var replacement = AccessTools.Method(typeof(ShuffleMoveOrderPatch), nameof(AddWithVisuals));
        int replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(add))
            {
                instruction.operand = replacement;
                replacements++;
            }
            yield return instruction;
        }
        if (replacements != 1)
            throw new InvalidOperationException("Expected one batch Add call in CardPileCmd.Shuffle.");
    }

    private static async Task<IReadOnlyList<CardPileAddResult>> AddWithVisuals(
        IEnumerable<CardModel> cards, CardPile pile, CardPilePosition position,
        AbstractModel? source, bool skipVisuals, bool isChangingOwners)
    {
        await CardPileCmd.Add(cards, pile, position, source,
            skipVisuals: false, isChangingOwners: isChangingOwners);

        // Add has already dispatched the pile notifications and visuals before
        // its hooks. Shuffle therefore has no deferred animations left to run.
        return Array.Empty<CardPileAddResult>();
    }
}
#endif
