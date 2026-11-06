using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

[Pool(typeof(ColorlessCardPool))]
public sealed class CoreEmptyShell() : CustomCardModel(0, CardType.Skill, CardRarity.Token, TargetType.Self, false)
{
    public override CardPoolModel VisualCardPool => ModelDb.CardPool<ColorlessCardPool>();
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public override string? CustomPortraitPath => V1.CoreApi.ResourceRoot + "images/cards/empty_shell.png";
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;
}
