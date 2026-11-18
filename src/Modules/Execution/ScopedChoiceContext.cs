using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

internal sealed class ScopedChoiceContext : PlayerChoiceContext, IAsyncDisposable
{
    private readonly PlayerChoiceContext _original;
    private int _open;
    internal ScopedChoiceContext(PlayerChoiceContext original)
    {
        _original = original;
#if STS2_CHOICE_OWNER
        foreach (var model in original.ModelStack?.Reverse() ?? []) PushModel(model);
#else
        if (original.LastInvolvedModel is { } model) PushModel(model);
#endif
    }

#if STS2_CHOICE_OWNER
    public override ulong? OwnerId => _original.OwnerId;
#endif
#if STS2_BETA
    public override GameActionType? GameActionType => _original.GameActionType;
#endif
#if STS2_CHOICE_OWNER
    public override async Task SignalPlayerChoiceBegun(Player chooser, PlayerChoiceOptions options)
    {
        await _original.SignalPlayerChoiceBegun(chooser, options);
#else
    public override async Task SignalPlayerChoiceBegun(PlayerChoiceOptions options)
    {
        await _original.SignalPlayerChoiceBegun(options);
#endif
        _open++;
    }
    public override async Task SignalPlayerChoiceEnded()
    {
        if (_open > 0) _open--;
        await _original.SignalPlayerChoiceEnded();
    }
    public async ValueTask DisposeAsync()
    {
        Exception? failure = null;
        while (_open > 0)
        {
            try { await SignalPlayerChoiceEnded(); }
            catch (Exception error) { failure = Cleanup.Combine(failure, error); }
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
