using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

[CustomID("KAREN_STAGE_REASON")]
[Pool(typeof(CurseCardPool))]
[ShineCard(3)]
public sealed class CoreStageReason() : CustomCardModel(1, CardType.Curse, CardRarity.Curse, TargetType.Self)
{
    public override int MaxUpgradeLevel => 0;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public override string? CustomPortraitPath => CoreApi.ResourceRoot + "images/cards/stage_reason.png";
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;
}
