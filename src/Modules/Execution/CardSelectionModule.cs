using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>Native selection and synchronization with invocation-local presentation and cleanup.</summary>
public sealed class CardSelectionModule : ICoreModule, ICardSelection
{
    private static readonly AsyncLocal<LocString?> Title = new();
    public string Id => "card-selection";
    public int Order => -300;
    public void Initialize(CoreContext context) => context.Services.Add<ICardSelection>(this);
    public async Task<CardModel?> Choose(PlayerChoiceContext context, IReadOnlyList<CardModel> cards,
        Player player, bool canSkip, LocString? title)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(player);
        if (cards.Count > 3) throw new ArgumentException("At most three choices are supported.", nameof(cards));
        if (cards.Count == 0) return null;
        var previous = Title.Value;
        Title.Value = title;
        try
        {
            await using var choice = new ChoiceContext(context);
            var selected = await CardSelectCmd.FromChooseACardScreen(choice, cards, player, canSkip);
            if (selected != null && !cards.Contains(selected))
                throw new InvalidOperationException("The selected card is not one of the offered choices.");
            if (selected == null && !canSkip)
                throw new InvalidOperationException("A required card choice returned no selection.");
            return selected;
        }
        finally { Title.Value = previous; }
    }
    private sealed class ChoiceContext : PlayerChoiceContext, IAsyncDisposable
    {
        private readonly PlayerChoiceContext _original;
        private int _open;
        internal ChoiceContext(PlayerChoiceContext original)
        {
            _original = original;
#if STS2_BETA
            foreach (var model in original.ModelStack?.Reverse() ?? []) PushModel(model);
#else
            if (original.LastInvolvedModel is { } model) PushModel(model);
#endif
        }

#if STS2_BETA
        public override ulong? OwnerId => _original.OwnerId;
#endif
#if STS2_BETA
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
            while (_open > 0) await SignalPlayerChoiceEnded();
        }
    }
    [HarmonyPatch(typeof(NChooseACardSelectionScreen), "_Ready")]
    private static class ChoiceTitle
    {
        private static void Postfix(NCommonBanner ____banner)
        {
            if (Title.Value is { } title) ____banner.label.SetTextAutoSize(title.GetFormattedText());
        }
    }
}
