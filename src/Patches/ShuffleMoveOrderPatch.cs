#if STS2_CARD_LOCATION
using HarmonyLib;
using ShoujoKageki.Core.Runtime.Modules.Execution;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Linq;
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
        var visualResults = AccessTools.Method(typeof(ShuffleMoveOrderPatch), nameof(AddWithVisualResults));
        var discardResults = AccessTools.Method(typeof(ShuffleMoveOrderPatch), nameof(DiscardVisualResults));
        var code = instructions.ToArray();
        int replacements = 0;
        for (int index = 0; index < code.Length; index++)
        {
            var instruction = code[index];
            bool discard = false;
            if (instruction.Calls(replacement)) replacements++;
            else if (instruction.Calls(add))
            {
                instruction.operand = replacement;
                replacements++;
            }
            else if (instruction.operand is MethodInfo forwarded
                && ForwardedCardCall.Adapt(forwarded, call => call == add, visualResults))
            {
                replacements++;
                discard = index + 1 >= code.Length || !code[index + 1].Calls(discardResults);
            }
            yield return instruction;
            if (discard) yield return new CodeInstruction(OpCodes.Call, discardResults);
        }
        if (replacements != 1)
            throw BoundaryDiagnostics.Mismatch("Expected one batch Add call in CardPileCmd.Shuffle.", TargetMethod(), code);
    }

    private static async Task<IReadOnlyList<CardPileAddResult>> AddWithVisuals(
        IEnumerable<CardModel> cards, CardPile pile, CardPilePosition position,
        AbstractModel? source, bool skipVisuals, bool isChangingOwners)
    {
        await AddWithVisualResults(cards, pile, position, source, skipVisuals, isChangingOwners);

        // Add has already dispatched the pile notifications and visuals before
        // its hooks. Shuffle therefore has no deferred animations left to run.
        return Array.Empty<CardPileAddResult>();
    }

    // Keep the native Add patch point late-bound when a forwarding body is JIT-compiled.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<IReadOnlyList<CardPileAddResult>> AddWithVisualResults(
        IEnumerable<CardModel> cards, CardPile pile, CardPilePosition position,
        AbstractModel? source, bool skipVisuals, bool isChangingOwners)
        => CardPileCmd.Add(cards, pile, position, source, skipVisuals: false, isChangingOwners: isChangingOwners);

    // A forwarding mod still receives the native results for its own hooks.
    // Only Shuffle discards the results of visuals that have already run.
    private static async Task<IReadOnlyList<CardPileAddResult>> DiscardVisualResults(
        Task<IReadOnlyList<CardPileAddResult>> result)
    {
        await result;
        return Array.Empty<CardPileAddResult>();
    }
}
#endif
