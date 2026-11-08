using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.Runtime.Modules.Shine;
public sealed class CardCopyService : ICoreModule, ICardCopyService
{
    public string Id => "card-copies";
    public int Order => -300;
    public void Initialize(CoreContext context) => context.Services.Add<ICardCopyService>(this);
    public CardModel Preview(CardModel source, CardCopyPurpose purpose) => ShineService.Instance.Copy(source, purpose);
    public CardModel ForDeck(CardModel source, Player owner, CardCopyPurpose purpose)
    {
        var snapshot = Preview(source, purpose);
        var copy = owner.RunState.LoadCard(snapshot.ToSerializable(), owner);
        if (purpose == CardCopyPurpose.Reacquire) ShineService.Instance.ResetLifecycle(copy);
        return copy;
    }
    public CardModel ForCombat(CardModel source, Player owner, CardCopyPurpose purpose, CardModel? permanentSource = null)
    {
        var combat = owner.Creature.CombatState ?? throw new InvalidOperationException("A combat is required.");
        var snapshot = Preview(source, purpose);
        if (snapshot.Owner != owner)
        {
            snapshot.Owner = null!;
            snapshot.Owner = owner;
        }
        snapshot.DeckVersion = purpose == CardCopyPurpose.TemporaryCopy ? null : permanentSource;
        var copy = combat.CloneCard(snapshot);
        copy.DeckVersion = snapshot.DeckVersion;
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
