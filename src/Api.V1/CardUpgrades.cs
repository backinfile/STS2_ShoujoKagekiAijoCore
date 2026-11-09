using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

public interface ICardUpgrades
{
    IDisposable Register(Func<CardModel, bool> matches, Func<CardModel, int, int, Task> upgraded);
    Task Upgrade(IEnumerable<CardModel> cards, CardPreviewStyle style);
}
public static class CardUpgrades
{
    public static Task Upgrade(CardModel card, CardPreviewStyle style = CardPreviewStyle.HorizontalLayout)
        => Upgrade([card], style);
    public static Task Upgrade(IEnumerable<CardModel> cards, CardPreviewStyle style = CardPreviewStyle.HorizontalLayout)
        => CoreApi.Get<ICardUpgrades>().Upgrade(cards, style);
}
