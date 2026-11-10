using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using ShoujoKageki.Core.V1;

namespace CoreRoleIntegration;

internal static class ShineDescriptionProbe
{
    internal static string Run(Player? player)
    {
        var introduction = CoreApi.Get<ICoreDiscovery>().Models
            .Where(item => typeof(CardModel).IsAssignableFrom(item.ModelType))
            .Select(item => ModelDb.GetById<CardModel>(ModelDb.GetId(item.ModelType)))
            .First(card => card.Id.Entry == "JUNNA_JK131");
        var label = new LocString("gameplay_ui", "CORE_SHINE_LABEL").GetFormattedText();
        var suffix = new LocString("gameplay_ui", "CORE_SHINE_SUFFIX").GetFormattedText();
        var rows = new List<object>();
        var passed = true;

        void Check(CardModel card, string name, bool preview, string number)
        {
            var before = Shine.GetState(card);
            var text = preview ? card.GetDescriptionForUpgradePreview() : card.GetDescriptionForPile(PileType.Deck);
            var expected = label + number + suffix;
            var pass = text.EndsWith(expected, StringComparison.Ordinal) && Shine.GetState(card) == before;
            passed &= pass;
            rows.Add(new { card = card.Id.ToString(), name, passed = pass, text, expected });
        }

        foreach (var model in new CardModel[] { introduction, ModelDb.Card<ResolutionProbeCard>() })
        {
            var card = (CardModel)model.MutableClone();
            card.Owner = player!;
            Shine.SetState(card, 9, 9);
            Check(card, "full regular", false, "9");
            card.UpgradeInternal();
            Check(card, "upgraded regular", false, "9");
            Check(card, "full upgrade preview", true, "[gold]9[/gold]");
            Shine.SetState(card, 2, 9);
            Check(card, "spent regular", false, "[red]2[/red]");
            Check(card, "spent upgrade preview restores maximum", true, "[gold]9[/gold]");
            Shine.SetState(card, 12, 9);
            Check(card, "overflow regular", false, "[gold]12[/gold]");
            Check(card, "overflow upgrade preview", true, "[gold]12[/gold]");
        }

        return JsonSerializer.Serialize(new { passed, rows });
    }
}
