using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.V1;
public interface ICardCopyService
{
    CardModel Preview(CardModel source, CardCopyPurpose purpose);
    CardModel ForDeck(CardModel source, Player owner, CardCopyPurpose purpose);
    CardModel ForCombat(CardModel source, Player owner, CardCopyPurpose purpose, CardModel? permanentSource = null);
    Task<CardPileAddResult> Acquire(CardModel source, Player owner, bool addToHand);
}
public static class CardCopies
{
    private static ICardCopyService Service => CoreApi.Get<ICardCopyService>();
    public static T Snapshot<T>(T source) where T : CardModel => (T)CoreApi.Get<ICardSnapshots>().Capture(source);
    public static CardModel Preview(CardModel source, CardCopyPurpose purpose) => Service.Preview(source, purpose);
    public static CardModel ForDeck(CardModel source, Player owner, CardCopyPurpose purpose) => Service.ForDeck(source, owner, purpose);
    public static CardModel ForCombat(CardModel source, Player owner, CardCopyPurpose purpose, CardModel? permanentSource = null) => Service.ForCombat(source, owner, purpose, permanentSource);
    public static Task<CardPileAddResult> Acquire(CardModel source, Player owner, bool addToHand = false) => Service.Acquire(source, owner, addToHand);
}
