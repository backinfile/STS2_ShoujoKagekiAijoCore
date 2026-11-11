using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

public sealed class PlayerChoicesModule : ICoreModule, IPlayerChoices
{
    public string Id => "player-choices";
    public int Order => -300;
    public void Initialize(CoreContext context) => context.Services.Add<IPlayerChoices>(this);

    public async Task<int> ChooseIndex(PlayerChoiceContext? context, Player player, int optionCount,
        Func<Task<int>> selectLocal)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(selectLocal);
        ArgumentOutOfRangeException.ThrowIfNegative(optionCount);
        if (optionCount == 0) return -1;
        var run = RunManager.Instance;
        var sync = run.PlayerChoiceSynchronizer;
        uint id = sync.ReserveChoiceId(player);
        await using var choice = context == null ? null : new ScopedChoiceContext(context);
        if (choice != null)
#if STS2_BETA
            await choice.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
#else
            await choice.SignalPlayerChoiceBegun(PlayerChoiceOptions.None);
#endif
        return await SynchronizedChoice.ChooseIndex(optionCount,
            LocalContext.IsMe(player) && run.NetService.Type != NetGameType.Replay,
            () => id, selectLocal,
            (choiceId, index) => sync.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromIndex(index)),
            async choiceId => (await sync.WaitForRemoteChoice(player, choiceId)).AsIndex());
    }
}
