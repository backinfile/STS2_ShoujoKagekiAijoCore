using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>Native selection and synchronization with invocation-local presentation and cleanup.</summary>
public sealed class CardSelectionModule : ICoreModule, ICardSelection, ICardOptions, ICardPicking
{
    private static readonly AsyncLocal<LocString?> Title = new();
    public string Id => "card-selection";
    public int Order => -300;
    public void Initialize(CoreContext context)
    {
        context.Services.Add<ICardSelection>(this);
        context.Services.Add<ICardOptions>(this);
        context.Services.Add<ICardPicking>(this);
    }
    public async Task<IReadOnlyList<CardModel>> Pick(PlayerChoiceContext context, Player player,
        IEnumerable<CardModel> candidates, CardPickRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Prompt);
        if (request.Minimum < 0 || request.Maximum < request.Minimum)
            throw new ArgumentOutOfRangeException(nameof(request));
        var cards = candidates.Distinct().ToArray();
        int maximum = Math.Min(request.Maximum, cards.Length);
        if (maximum == 0) return [];
        var piles = cards.ToDictionary(card => card, card => card.Pile);
        var preferences = new CardSelectorPrefs(request.Prompt, Math.Min(request.Minimum, maximum), maximum);
        await using var choice = new ChoiceContext(context);
        var selected = request.PreferHand && cards.All(card => card.Pile?.Type == MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand && card.Owner == player)
            ? await CardSelectCmd.FromHand(choice, player, preferences, cards.Contains, request.Source!)
            : await CardSelectCmd.FromSimpleGrid(choice, cards, player, preferences);
        var result = selected.ToArray();
        if (result.Length > maximum || result.Length < Math.Min(request.Minimum, maximum) || result.Distinct().Count() != result.Length || result.Any(card => !piles.ContainsKey(card)))
            throw new InvalidOperationException("The selected cards do not match the offered selection.");
        return result.Where(card => !card.HasBeenRemovedFromState && ReferenceEquals(card.Pile, piles[card])).ToArray();
    }
    public CardModel Create(CardModel canonical, Player owner, bool upgrade = false)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(owner);
        var option = CoreApi.Get<ICardSnapshots>().Capture(canonical);
        CardModelApi.AssignRecipient(option, owner);
        if (upgrade && option.IsUpgradable) CardCmd.Upgrade(option);
        return option;
    }
    Task<CardModel?> ICardOptions.Choose(PlayerChoiceContext context, IReadOnlyList<CardModel> cards,
        Player player, bool canSkip, LocString? title)
        => ChooseCore(context, cards, player, canSkip, title);

    public Task<CardModel?> Choose(PlayerChoiceContext context, IReadOnlyList<CardModel> cards,
        Player player, bool canSkip, LocString? title)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(player);
        if (cards.Count > 3) throw new ArgumentException("At most three choices are supported.", nameof(cards));
        if (cards.Count == 0) return Task.FromResult<CardModel?>(null);
        return ChooseCore(context, cards, player, canSkip, title);
    }
    private static async Task<CardModel?> ChooseCore(PlayerChoiceContext context, IReadOnlyList<CardModel> cards,
        Player player, bool canSkip, LocString? title)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(player);
        if (cards.Count == 0) return null;
        var previous = Title.Value;
        Title.Value = title;
        try
        {
            await using var choice = new ChoiceContext(context);
            CardModel? selected;
            if (cards.Count <= 3)
                selected = await CardSelectCmd.FromChooseACardScreen(choice, cards, player, canSkip);
            else
            {
                var preferences = new CardSelectorPrefs(title ?? new LocString("gameplay_ui", "CORE_SELECT_OPTION"), canSkip ? 0 : 1, 1);
                selected = (await CardSelectCmd.FromSimpleGrid(choice, cards, player, preferences)).SingleOrDefault();
            }
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
