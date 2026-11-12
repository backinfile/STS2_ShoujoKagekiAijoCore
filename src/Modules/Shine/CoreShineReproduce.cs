using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using MegaCrit.Sts2.Core.Localization;
using ShoujoKageki.Core.V1;


using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

/// <summary>
/// 歌唱吧舞蹈吧互相争斗吧 - 1/0费Token技能牌，Shine 1
/// 从闪耀耗尽牌堆中选择1张牌加入牌组
/// </summary>
[CustomID("KAREN_SHINE_REPRODUCE")]
[Pool(typeof(TokenCardPool))]
[ShineCard(1)]
public sealed class CoreShineReproduce : CustomCardModel
{

    public override CardPoolModel VisualCardPool => ModelDb.CardPool<ColorlessCardPool>();

    public CoreShineReproduce() : base(1, CardType.Skill, CardRarity.Token, TargetType.Self) { }

    public override string? CustomPortraitPath => CoreApi.ResourceRoot + "images/cards/shine_reproduce.png";
    public override string? CustomBannerMaterialPath => "res://materials/cards/banners/card_banner_rare_mat.tres";

    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [new HoverTip(
            new LocString("cards", "KAREN_SHINE_REPRODUCE.tip.title"),
            new LocString("cards", "KAREN_SHINE_REPRODUCE.tip").GetFormattedText()
        )];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (CombatState == null) return;

        var cards = ShoujoKageki.Core.V1.Shine.GetDepletedModels(Owner)
            .Where(c => c is not CoreShineReproduce)
            .Select(card => ShoujoKageki.Core.V1.CardCopies.Preview(card, ShoujoKageki.Core.V1.CardCopyPurpose.Reacquire))
            .ToList();
        if (cards.Count == 0) return;

        var selected = await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            cards,
            Owner,
            new CardSelectorPrefs(new LocString("cards", "KAREN_SHINE_REPRODUCE.select_title"), 0, 1)
        );

        var card = selected.FirstOrDefault();
        if (card == null) return;
        var result = await ShoujoKageki.Core.V1.CardCopies.Acquire(card, Owner, addToHand: true);
        CardCmd.PreviewCardPileAdd(result, 1.2f, CardPreviewStyle.MessyLayout);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
