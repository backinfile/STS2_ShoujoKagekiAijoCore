using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>Resource payment surrounds the native automatic-play pipeline.</summary>
public sealed class CardExecutionModule : ICoreModule, ICardExecution
{
    private sealed class Invocation(CardModel card, CardExecutionRequest request, ResourceInfo resources)
    {
        internal readonly CardModel Card = card;
        internal ResourceInfo Resources = resources;
        internal readonly CardExecutionRequest Request = request;
        internal bool PaymentPrepared;
        internal bool NativeStarted;
        internal bool Entered;
    }
    private static readonly AsyncLocal<Invocation?> Current = new();
    public string Id => "card-execution";
    public int Order => -300;
    public void Initialize(CoreContext context) => context.Services.Add<ICardExecution>(this);

    public async Task<bool> Play(PlayerChoiceContext context, CardModel card, CardExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Payment)) throw new ArgumentOutOfRangeException(nameof(request));
        var owner = card.Owner;
        var combat = owner?.Creature.CombatState;
        if (combat == null || owner!.PlayerCombatState == null || owner.Creature.IsDead || CombatManager.Instance.IsOverOrEnding)
            return false;
        if (card.Keywords.Contains(CardKeyword.Unplayable))
            return false;
        if (request.Target != null && card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly && !card.IsValidTarget(request.Target)) return false;
        if (request.Payment == CardPayment.Paid && !owner.PlayerCombatState.HasEnoughResourcesFor(card, out _))
            return false;

        int energyValue = card.EnergyCost.CostsX
            ? request.Payment == CardPayment.Paid || request.CaptureAvailableX ? owner.PlayerCombatState.Energy : 0
            : Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All));
        int starValue = Math.Max(0, card.GetStarCostWithModifiers());
        int energySpent = 0, starsSpent = 0;
        if (request.Payment == CardPayment.Free && card.EnergyCost.CostsX)
            card.EnergyCost.CapturedXValue = energyValue;
        var resources = new ResourceInfo
        {
            EnergySpent = energySpent,
            EnergyValue = request.Payment == CardPayment.Paid || request.PreserveEnergyValue ? energyValue : 0,
            StarsSpent = starsSpent,
            StarValue = starValue
        };
        var invocation = new Invocation(card, request, resources);
        var previous = Current.Value;
        Current.Value = invocation;
        try
        {
            await CardCmd.AutoPlay(context, card, request.Target,
                skipXCapture: request.Payment == CardPayment.Paid || !request.CaptureAvailableX);
            return invocation.Entered;
        }
        catch (PaymentRejectedException)
        {
            return false;
        }
        finally { Current.Value = previous; }
    }

    private sealed class PaymentRejectedException : Exception;

    private static async Task BeforeNativeAutoPlayed(ICombatState combat, CardModel card, Creature? target, AutoPlayType type)
    {
        var invocation = Current.Value;
        await Hook.BeforeCardAutoPlayed(combat, card, target, type);
        if (invocation == null || invocation.PaymentPrepared || !ReferenceEquals(invocation.Card, card)) return;
        invocation.PaymentPrepared = true;
        if (invocation.Request.Payment != CardPayment.Paid) return;
        if (!card.Owner.PlayerCombatState!.HasEnoughResourcesFor(card, out _)) throw new PaymentRejectedException();
        var (energy, stars) = await card.SpendResources();
        invocation.Resources = new ResourceInfo
        {
            EnergySpent = energy, EnergyValue = invocation.Resources.EnergyValue,
            StarsSpent = stars, StarValue = invocation.Resources.StarValue
        };
    }

    private static Task EnterNativePlay(CardModel card, PlayerChoiceContext context, Creature? target,
        bool isAutoPlay, ResourceInfo resources, bool skipCardPileVisuals)
    {
        var invocation = Current.Value;
        if (invocation != null && !invocation.Entered && ReferenceEquals(invocation.Card, card))
        {
            invocation.Entered = true;
            resources = invocation.Resources;
        }
        return card.OnPlayWrapper(context, target, isAutoPlay, resources, skipCardPileVisuals);
    }

    [HarmonyPatch]
    private static class NativeInvocation
    {
        private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(
            AccessTools.Method(typeof(CardCmd), nameof(CardCmd.AutoPlay)));
        private static readonly FieldInfo State = AccessTools.Field(TargetMethod().DeclaringType!, "<>1__state");
        private static readonly FieldInfo Card = AccessTools.Field(TargetMethod().DeclaringType!, "card");
        private static void Prefix(object __instance, out Invocation? __state)
        {
            __state = Current.Value;
            if (__state == null) return;
            if ((int)State.GetValue(__instance)! != -1) return;
            var card = (CardModel)Card.GetValue(__instance)!;
            if (__state.NativeStarted || !ReferenceEquals(__state.Card, card)) Current.Value = null;
            else __state.NativeStarted = true;
        }
        private static void Postfix(Invocation? __state) => Current.Value = __state;
        private static Exception? Finalizer(Exception? __exception, Invocation? __state)
        {
            Current.Value = __state;
            return __exception;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int hooks = 0, entries = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method)
                {
                    string? replacement = null;
                    if (method.DeclaringType == typeof(Hook) && method.Name == nameof(Hook.BeforeCardAutoPlayed))
                    {
                        replacement = nameof(BeforeNativeAutoPlayed);
                        hooks++;
                    }
                    if (method.DeclaringType == typeof(CardModel) && method.Name == nameof(CardModel.OnPlayWrapper))
                    {
                        replacement = nameof(EnterNativePlay);
                        entries++;
                    }
                    if (replacement != null)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(CardExecutionModule), replacement);
                    }
                }
                yield return instruction;
            }
            if (hooks != 1 || entries != 1)
                throw new InvalidOperationException($"Core automatic play boundaries: hooks={hooks}, entries={entries}");
        }
    }
}
