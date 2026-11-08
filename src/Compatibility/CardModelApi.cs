using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime;

internal static class CardModelApi
{
    /// <summary>Only new copies and detached snapshots enter here, before they are placed in a pile.</summary>
    internal static void AssignRecipient(CardModel snapshot, Player owner)
    {
#if STS2_BETA
        snapshot.GiveToAnotherPlayer(owner);
#else
        if (snapshot.Owner == owner) return;
        snapshot.Owner = null!;
        snapshot.Owner = owner;
#endif
    }
}
