using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

/// <summary>
/// Local presentation of already generated single-card rewards. CombatRoom keeps the
/// original rewards for native saves and synchronization; only the rewards screen groups the UI.
/// </summary>
public sealed class CoreShineCardRewardChoice : Reward
{
    private readonly IReadOnlyList<CoreShineCardReward> _rewards;
    private bool _resolved;

    public CoreShineCardRewardChoice(IReadOnlyList<CoreShineCardReward> rewards) : base(rewards[0].Player)
    {
        _rewards = rewards;
        Cards = rewards.Select(reward => reward.Card).ToList();
    }

    public IReadOnlyList<CardModel> Cards { get; }
    protected override RewardType RewardType => RewardType.None;
    public override int RewardsSetIndex => 4;
    public override bool IsPopulated => Cards.Count > 0;
    protected override string IconPath => V1.CoreApi.ResourceRoot + "images/powers/shine_reward_power.png";
    public override LocString Description
    {
        get
        {
            var description = new LocString("gameplay_ui", "KAREN_SHINE_CARD_REWARD");
            description.Add("Count", Cards.Count);
            return description;
        }
    }

    public override Control CreateIcon()
    {
        var icon = base.CreateIcon();
        if (icon != null) icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        return icon!;
    }

    public override void Populate() { }
    public override void MarkContentAsSeen()
    {
        foreach (var reward in _rewards) reward.MarkContentAsSeen();
    }

    protected override Task<bool> OnSelect() => SelectCards();

    public async Task<bool> SelectCards()
    {
        if (_resolved) return true;
        var prompt = new LocString("gameplay_ui", "KAREN_SHINE_CARD_REWARD_SELECT");
        var prefs = new CardSelectorPrefs(prompt, 0, Cards.Count) { RequireManualConfirmation = true };
        // A plain multi-select grid has no CardRewardAlternative generation or relic conversions.
        var screen = NSimpleCardSelectScreen.Create(Cards, prefs);
        NOverlayStack.Instance!.Push(screen);
        var selected = (await screen.CardsSelected()).ToHashSet();
        _resolved = true;
        foreach (var reward in _rewards)
        {
            if (selected.Contains(reward.Card))
                await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
        }
        return true;
    }

    // Unselected originals are recorded exactly once by the native RewardsSet skip flow.
}
