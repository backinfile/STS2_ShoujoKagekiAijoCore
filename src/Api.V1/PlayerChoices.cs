using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace ShoujoKageki.Core.V1;

/// <summary>Synchronizes a custom choice using the native player-choice channel.</summary>
public interface IPlayerChoices
{
    /// <summary>The local callback returns an offered index, or -1 to cancel.
    /// A supplied context is balanced on completion and failure. Empty choices return -1.</summary>
    Task<int> ChooseIndex(PlayerChoiceContext? context, Player player, int optionCount, Func<Task<int>> selectLocal);
}

public static class PlayerChoices
{
    public static Task<int> ChooseIndex(PlayerChoiceContext? context, Player player, int optionCount,
        Func<Task<int>> selectLocal)
        => CoreApi.Get<IPlayerChoices>().ChooseIndex(context, player, optionCount, selectLocal);
}
