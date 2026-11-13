using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace ShoujoNativeReferenceExport;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    private static bool exported;
    public static void Initialize() => new Harmony("shoujo.native-reference-export").PatchAll();

    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
    private static class MainMenuReady
    {
        private static void Postfix() => Callable.From(Export).CallDeferred();
    }

    private static void Export()
    {
        if (exported) return;
        exported = true;
        string destination = System.Environment.GetEnvironmentVariable("SHOUJO_NATIVE_EXPORT")
            ?? throw new InvalidOperationException("Set SHOUJO_NATIVE_EXPORT to an ignored output path.");
        try
        {
            LocManager.Instance.SetLanguage("zhs");
            var errors = new List<object>();
            var cards = new List<object>();
            foreach (var canonical in ModelDb.AllCards.OrderBy(card => card.Id.Entry))
            {
                try
                {
                    var card = canonical.ToMutable();
                    string text = card.GetDescriptionForPile(PileType.None);
                    object cost = card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetWithModifiers(CostModifiers.None);
                    object? stars = card.HasStarCostX ? "X" : card.BaseStarCost < 0 ? null : card.BaseStarCost;
                    string title = card.Title;
                    var keywords = card.Keywords.Select(keyword => keyword.ToString()).ToArray();
                    if (card.MaxUpgradeLevel > 0)
                    {
                        card.UpgradeInternal();
                        card.FinalizeUpgradeInternal();
                    }
                    cards.Add(new { id = card.Id.Entry, name = title, text, cost, starCost = stars,
                        upgradedText = card.GetDescriptionForPile(PileType.None),
                        upgradedCost = card.EnergyCost.CostsX ? (object)"X" : card.EnergyCost.GetWithModifiers(CostModifiers.None),
                        upgradedStarCost = card.HasStarCostX ? (object)"X" : card.BaseStarCost < 0 ? null : (object)card.BaseStarCost,
                        keywords, upgradedKeywords = card.Keywords.Select(keyword => keyword.ToString()).ToArray(),
                        type = card.Type.ToString(), rarity = card.Rarity.ToString(), pool = card.Pool.Id.Entry });
                }
                catch (Exception error) { errors.Add(new { kind = "card", id = canonical.Id.Entry, error = error.ToString() }); }
            }
            var relics = new List<object>();
            foreach (var canonical in ModelDb.AllRelics.OrderBy(relic => relic.Id.Entry))
            {
                try
                {
                    var relic = canonical.ToMutable();
                    relics.Add(new { id = relic.Id.Entry, name = relic.Title.GetFormattedText(),
                        text = relic.DynamicDescription.GetFormattedText(), rarity = relic.Rarity.ToString() });
                }
                catch (Exception error) { errors.Add(new { kind = "relic", id = canonical.Id.Entry, error = error.ToString() }); }
            }
            File.WriteAllText(destination, JsonSerializer.Serialize(new { gameVersion = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "release_info.json"))).RootElement.GetProperty("version").GetString(), locale = LocManager.Instance.Language, assemblySHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CardModel).Assembly.Location))).ToLowerInvariant(), cards, relics, errors }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[NativeReferenceExport] cards={cards.Count} relics={relics.Count} errors={errors.Count}");
        }
        catch (Exception error) { File.WriteAllText(destination + ".error", error.ToString()); }
    }
}