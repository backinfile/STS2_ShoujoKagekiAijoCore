using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.V1;

/// <summary>Captures detached runtime data without acquiring a card or replacing its model.</summary>
public interface ICardSnapshots
{
    CardModel Capture(CardModel source);
}
