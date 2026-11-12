using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

/// <summary>Shine presentation with native single-card claiming, history and serialization.</summary>
public sealed class CoreShineCardReward(CardModel card, Player player) : SpecialCardReward(card, player)
{
    public CardModel Card { get; } = card;
    protected override string IconPath => V1.CoreApi.ResourceRoot + "images/powers/shine_reward_power.png";
    public override LocString Description
    {
        get
        {
            var description = new LocString("gameplay_ui", "KAREN_SHINE_CARD_REWARD");
            description.Add("Count", 1);
            return description;
        }
    }

    // Shine reward choices stay outside ordinary card-choice statistics.
    // Native acquisition still records CardsGained for ownership and win-rate data.
    public override void OnSkipped() { }

    public override Control CreateIcon()
    {
        var icon = base.CreateIcon();
        if (icon != null) icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        return icon!;
    }
}
