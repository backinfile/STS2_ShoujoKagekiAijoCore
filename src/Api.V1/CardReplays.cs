using MegaCrit.Sts2.Core.Models;
namespace ShoujoKageki.Core.V1;
public interface ICardReplayService
{
    int GetNext(CardModel card);
    void SetNext(CardModel card, int count);
}
public static class CardReplays
{
    public static int GetNext(CardModel card) => CoreApi.Get<ICardReplayService>().GetNext(card);
    public static void SetNext(CardModel card, int count) => CoreApi.Get<ICardReplayService>().SetNext(card, count);
}
