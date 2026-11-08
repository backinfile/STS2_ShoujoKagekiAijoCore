using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

/// <summary>Separates detached snapshots, combat copies and permanent acquisitions.</summary>
public sealed class CardCopyService : ICoreModule, ICardCopyService, ICardSnapshots
{
    public string Id => "card-copies";
    public int Order => -300;
    public void Initialize(CoreContext context)
    {
        context.Services.Add<ICardCopyService>(this);
        context.Services.Add<ICardSnapshots>(this);
    }
    public CardModel Capture(CardModel source) => CaptureSnapshot(source);
    internal static CardModel CaptureSnapshot(CardModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var scope = ShineService.AllowSnapshotCopy();
        var snapshot = (CardModel)source.MutableClone();
        ShineService.Instance.PreserveSnapshotOrigin(source, snapshot);
        return snapshot;
    }
    public CardModel Preview(CardModel source, CardCopyPurpose purpose) => CopyDetached(source, purpose);
    internal static CardModel CopyDetached(CardModel source, CardCopyPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        var shine = ShineService.Instance;
        if (purpose != CardCopyPurpose.Reacquire && shine.IsDepletedCopySource(source))
            return shine.CreateEmptyShell(source);
        CardModel copy;
        if (purpose == CardCopyPurpose.Reacquire)
        {
            using var scope = ShineService.AllowSnapshotCopy();
            copy = CardModel.FromSerializable((source.IsMutable ? source : CaptureSnapshot(source)).ToSerializable());
            if (source.IsMutable) copy.Owner = source.Owner;
        }
        else copy = CaptureSnapshot(source);
        shine.InitializeCopy(source, copy, purpose);
        return copy;
    }
    public CardModel ForDeck(CardModel source, Player owner, CardCopyPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var snapshot = Preview(source, purpose);
        var copy = owner.RunState.LoadCard(snapshot.ToSerializable(), owner);
        ShineService.Instance.PreserveSnapshotOrigin(snapshot, copy);
        if (purpose == CardCopyPurpose.Reacquire) ShineService.Instance.ResetLifecycle(copy);
        return copy;
    }
    public CardModel ForCombat(CardModel source, Player owner, CardCopyPurpose purpose, CardModel? permanentSource = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var combat = owner.Creature.CombatState ?? throw new InvalidOperationException("A combat is required.");
        if (permanentSource != null && (permanentSource.Owner != owner || !owner.Deck.Cards.Contains(permanentSource)))
            throw new ArgumentException("The permanent source must belong to the recipient's deck.", nameof(permanentSource));
        CardModel copy;
        if (purpose == CardCopyPurpose.TemporaryCopy && source.IsMutable && source.Pile?.IsCombatPile == true
            && ReferenceEquals(source.Owner?.Creature.CombatState, combat) && !ShineService.Instance.IsDepletedCopySource(source))
        {
            using var scope = ShineService.AllowSnapshotCopy();
            copy = source.CreateClone();
            CardModelApi.AssignRecipient(copy, owner);
        }
        else
        {
            var snapshot = Preview(source, purpose);
            CardModelApi.AssignRecipient(snapshot, owner);
            copy = purpose == CardCopyPurpose.TemporaryCopy ? snapshot.CreateClone() : combat.CloneCard(snapshot);
        }
        copy.DeckVersion = purpose == CardCopyPurpose.TemporaryCopy ? null : permanentSource;
        if (copy.Id == source.Id) ShineService.Instance.InitializeCopy(source, copy, purpose);
        return copy;
    }
    public async Task<CardPileAddResult> Acquire(CardModel source, Player owner, bool addToHand)
    {
        var copy = ForDeck(source, owner, CardCopyPurpose.Reacquire);
        bool accepted = false;
        try
        {
            var result = await CardPileCmd.Add(copy, PileType.Deck);
            accepted = result.success;
            if (accepted && addToHand && owner.Creature.CombatState != null)
            {
                var actual = ForCombat(result.cardAdded, owner, CardCopyPurpose.Transfer, result.cardAdded);
                try { await CardPileCmd.Add(actual, PileType.Hand); }
                finally { if (actual.Pile == null) actual.RemoveFromState(); }
            }
            return result;
        }
        finally { if (!accepted && copy.Pile == null) copy.RemoveFromState(); }
    }
}
