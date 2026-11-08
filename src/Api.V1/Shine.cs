using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Combat;

namespace ShoujoKageki.Core.V1;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ShineCardAttribute(int uses) : Attribute
{
    public int Uses { get; } = uses > 0 ? uses : throw new ArgumentOutOfRangeException(nameof(uses));
}
public enum CardResolutionKind { NativePlay, Arrow, Effect }
public enum CardCopyPurpose { TemporaryCopy, Transfer, Reacquire }
public sealed record ShineState(int Remaining, int Maximum, bool Temporary, bool PendingDepletion);
public sealed record DepletedCard(string ModelId, int Upgrades, int Maximum, string LifecycleId);
public sealed record ShineDepletionContext(CardModel Card, CardModel? PermanentCard, Player Player, string LifecycleId)
{
    public PlayerChoiceContext? ChoiceContext { get; init; }
    public ICombatState? CombatState { get; init; }
}
public interface IShineDepletionHandler
{
    Task OnShineDepleted(ShineDepletionContext context);
}
public interface IShineResolutionHandler
{
    Task AfterShineResolution(PlayerChoiceContext context, MegaCrit.Sts2.Core.Entities.Cards.CardPlay play);
}
public interface IShineDepletionListener
{
    Task OnShineDepleted(ShineDepletionContext context);
}
public interface IShinePresentation
{
    bool IsDepletionAnimating(CardModel card);
}
public interface IShineService
{
    ShineState? GetState(CardModel card);
    void SetState(CardModel card, int remaining, int maximum, bool temporary = false);
    void ChangeMaximum(CardModel card, int amount);
    void RestoreToMaximum(CardModel card);
    IReadOnlyList<CardModel> GetDepletedModels(Player player);
    CardModel Reacquire(Player player, DepletedCard card);
    void RegisterDeferredResolutionPredicate(Func<CardModel, bool> predicate);
    bool IsDeferredResolution(CardModel card);
    Task ResolveAsync(CardModel card, PlayerChoiceContext context, MegaCrit.Sts2.Core.Entities.Cards.CardPlay play, Func<Task> resolution, CardResolutionKind kind);
    void GrantTemporary(CardModel card, int uses);
    void DepleteAfterCurrentResolution(CardModel card);
    IReadOnlyList<DepletedCard> GetDepletedCards(Player player);
    Task ResolveAsync(CardModel card, Func<Task> resolution, CardResolutionKind kind);
    IAsyncDisposable BeginResolutionBatch();
    CardModel Copy(CardModel card, CardCopyPurpose purpose);
}
public static class Shine
{
    private static IShineService Service => CoreApi.Get<IShineService>();
    public static bool IsDepletionAnimating(CardModel card) => CoreApi.Get<IShinePresentation>().IsDepletionAnimating(card);
    public const string RulesetId = "core-shine@1";
    public static void SetState(CardModel card, int remaining, int maximum, bool temporary = false) => Service.SetState(card, remaining, maximum, temporary);
    public static void ChangeMaximum(CardModel card, int amount) => Service.ChangeMaximum(card, amount);
    public static void RestoreToMaximum(CardModel card) => Service.RestoreToMaximum(card);
    public static IReadOnlyList<CardModel> GetDepletedModels(Player player) => Service.GetDepletedModels(player);
    public static CardModel Reacquire(Player player, DepletedCard card) => Service.Reacquire(player, card);
    public static bool IsDeferredResolution(CardModel card) => Service.IsDeferredResolution(card);
    public static Task ResolveAsync(CardModel card, PlayerChoiceContext context, MegaCrit.Sts2.Core.Entities.Cards.CardPlay play, Func<Task> resolution, CardResolutionKind kind = CardResolutionKind.Arrow)
        => Service.ResolveAsync(card, context, play, resolution, kind);
    public static void RegisterDeferredResolutionPredicate(Func<CardModel, bool> predicate) => Service.RegisterDeferredResolutionPredicate(predicate);
    public static bool IsShineCard(CardModel card) => GetState(card) != null;
    public static ShineState? GetState(CardModel card) => Service.GetState(card);
    public static void GrantTemporary(CardModel card, int uses) => Service.GrantTemporary(card, uses);
    public static void DepleteAfterCurrentResolution(CardModel card) => Service.DepleteAfterCurrentResolution(card);
    public static IReadOnlyList<DepletedCard> GetDepletedCards(Player player) => Service.GetDepletedCards(player);
    public static int CountDepletedKinds(Player player) => GetDepletedCards(player).Select(x => x.ModelId).Distinct().Count();
    public static Task ResolveAsync(CardModel card, Func<Task> resolution, CardResolutionKind kind = CardResolutionKind.Arrow)
        => Service.ResolveAsync(card, resolution, kind);
    public static IAsyncDisposable BeginResolutionBatch() => Service.BeginResolutionBatch();
    public static CardModel Copy(CardModel card, CardCopyPurpose purpose) => Service.Copy(card, purpose);
}

public interface IShinePileView
{
    MegaCrit.Sts2.Core.Nodes.Screens.NCardPileScreen Show(Player player);
}
