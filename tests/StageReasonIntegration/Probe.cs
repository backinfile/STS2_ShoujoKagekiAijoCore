using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using ShoujoKageki.Core.V1;

namespace CoreStageReasonProbe;

[ModInitializer(nameof(Initialize))]
public static class Startup
{
    public static void Initialize() { }
}

public sealed class StageReasonCheckCommand : AbstractConsoleCmd
{
    private static string _result = "idle";
    public override string CmdName => "core_stage_reason_check";
    public override string Args => "[status]";
    public override string Description => "Check shared curse registration, resources, saves and native depletion.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? player, string[] args)
    {
        if (args.FirstOrDefault() == "status") return new(true, _result);
        if (player?.Creature.CombatState == null) return new(false, "Combat required.");
        _result = "running";
        _ = Run(player);
        return new(true, "Started; query status.");
    }

    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        void Check(string name, bool pass)
        {
            rows.Add(new { name, pass });
            if (!pass) throw new InvalidOperationException(name);
        }
        try
        {
            var id = new ModelId("CARD", "KAREN_STAGE_REASON");
            var model = ModelDb.GetById<CardModel>(id);
            Check("legacy ID resolves to the shared Core model", model.GetType().Name == "CoreStageReason"
                && model.GetType().Assembly.GetName().Name!.StartsWith("ShoujoKagekiAijoCore.Runtime."));
            Check("model discovery attributes the card to Core", CoreApi.Get<ICoreDiscovery>().Models
                .Single(entry => entry.ModelType == model.GetType()).ModId == CoreApi.ModId);
            Check("Karen no longer defines the migrated model", AppDomain.CurrentDomain.GetAssemblies()
                .All(assembly => assembly.GetType("ShoujoKagekiAijoKaren.src.Models.Cards.KarenStageReason") == null));
            Check("one copy in the global curse pool", ModelDb.CardPool<CurseCardPool>().AllCards.Count(card => card.Id == id) == 1);
            Check("available to native random curse sources", ModelDb.CardPool<CurseCardPool>()
                .GetUnlockedCards(UnlockState.all, player.RunState.CardMultiplayerConstraint).Count(card => card.Id == id) == 1);
            Check("curse frame and pool", model.Pool is CurseCardPool && model.VisualCardPool is CurseCardPool);
            Check("excluded from character, colorless and token pools", !player.Character.CardPool.AllCards
                .Concat(ModelDb.CardPool<ColorlessCardPool>().AllCards).Concat(ModelDb.CardPool<TokenCardPool>().AllCards).Any(card => card.Id == id));
            Check("excluded from shine rewards", CoreApi.Get<IShineRewards>().GetCandidates(player).All(card => card.Id != id));
            Check("original rules", model.Type == CardType.Curse && model.Rarity == CardRarity.Curse
                && model.MaxUpgradeLevel == 0 && model.CanonicalKeywords.SequenceEqual([CardKeyword.Exhaust]));
            Check("original shine budget", Shine.GetState(model) is { Remaining: 3, Maximum: 3 });
            Check("original portrait loads from Core", model.PortraitPath.StartsWith(CoreApi.ResourceRoot)
                && ResourceLoader.Load<Texture2D>(model.PortraitPath) is { } portrait && portrait.GetWidth() > 0);
            foreach (var (language, title) in new[] { ("zhs", "\u5b58\u5728\u4e8e\u821e\u53f0\u7684\u7406\u7531"),
                ("eng", "A Reason to Take the Stage"), ("jpn", "\u821e\u53f0\u306b\u7acb\u3064\u7406\u7531") })
            {
                using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(CoreApi.ResourceRoot + $"localization/{language}/cards.json"));
                Check("original localization: " + language, document.RootElement.GetProperty(id.Entry + ".title").GetString() == title
                    && document.RootElement.GetProperty(id.Entry + ".description").GetString() == "");
            }
            foreach (var language in new[] { "zhs", "eng", "jpn" })
            {
                string oldLocalization = $"res://ShoujoKagekiAijoKaren/localization/{language}/cards.json";
                if (!Godot.FileAccess.FileExists(oldLocalization)) continue;
                using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(oldLocalization));
                Check("Karen package no longer owns localization: " + language,
                    !document.RootElement.TryGetProperty(id.Entry + ".title", out _)
                    && !document.RootElement.TryGetProperty(id.Entry + ".description", out _));
            }
            Check("Karen portrait removed from the mounted packs", !ResourceLoader.Exists("res://images/packed/card_portraits/curse/karen_stage_reason.png"));
            Check("missing save fields initialize three uses", Shine.GetState(CardModel.FromSerializable(new SerializableCard { Id = id }))
                is { Remaining: 3, Maximum: 3 });
            foreach (var prefix in new[] { "karen", "core" })
            {
                var save = new SerializableCard { Id = id, Props = new() { ints = [
                    new("CardModel_" + prefix + "_shine_current", 2), new("CardModel_" + prefix + "_shine_max", 3)] } };
                var restored = CardModel.FromSerializable(save);
                Check(prefix + " saved budget survives restore", Shine.GetState(restored) is { Remaining: 2, Maximum: 3 }
                    && restored.ToSerializable().Id == id);
            }
            var mixedSave = new SerializableCard { Id = id, Props = new() { ints = [
                new("CardModel_karen_shine_current", 1), new("CardModel_karen_shine_max", 3),
                new("CardModel_core_shine_current", 2), new("CardModel_core_shine_max", 3)] } };
            Check("Core budget wins when old and new fields coexist", Shine.GetState(CardModel.FromSerializable(mixedSave))
                is { Remaining: 2, Maximum: 3 });
            var permanent = player.RunState.CreateCard(model, player);
            await CardPileCmd.Add(permanent, PileType.Deck);
            var card = player.Creature.CombatState!.CreateCard(model, player);
            card.DeckVersion = permanent;
            Check("one energy", card.EnergyCost.GetWithModifiers(CostModifiers.Local) == 1);
            var context = new ThrowingPlayerChoiceContext();
            for (int remaining = 2; remaining >= 0; remaining--)
            {
                await CardPileCmd.Add(card, PileType.Hand);
                await CardCmd.AutoPlay(context, card, player.Creature);
                Check("native play remaining " + remaining, Shine.GetState(card)?.Remaining == remaining);
                if (remaining > 0) Check("exhaust preserves permanent budget " + remaining,
                    card.Pile?.Type == PileType.Exhaust && Shine.GetState(permanent)?.Remaining == remaining);
            }
            Check("third use removes permanent curse", !player.Deck.Cards.Contains(permanent));
            var archived = Shine.GetDepletedCards(player).Single(entry => entry.ModelId == id.ToString());
            var restoredCard = Shine.Reacquire(player, archived);
            Check("archive keeps old ID and reacquires full budget", restoredCard.Id == id
                && Shine.GetState(restoredCard) is { Remaining: 3, Maximum: 3 });
            _result = JsonSerializer.Serialize(new { passed = true, rows });
        }
        catch (Exception error) { _result = JsonSerializer.Serialize(new { passed = false, rows, error = error.ToString() }); }
    }
}
