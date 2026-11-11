using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;
using ShoujoKagekiJunna.UI;

namespace CoreRoleIntegration;

public sealed class ShineIssuesCommand : AbstractConsoleCmd
{
    private static bool _drawing;
    private static string _result = "idle";
    public override string CmdName => "shine_issues";
    public override string Args => "preview | archive | race | retarget | replay | selfkill | status";
    public override string Description => "Targeted regression checks for Shine issue notes.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args)
    {
        if (args.FirstOrDefault() == "status") return new(true, _result);
        if (player == null) return new(false, "Run required.");
        if (!_drawing) { _drawing = true; ((Godot.SceneTree)Godot.Engine.GetMainLoop()).ProcessFrame += () => Godot.RenderingServer.ForceDraw(false); }
        if (ShineIssueViews.TryHandle(player, args.FirstOrDefault() ?? "", out var viewResult)) return viewResult;
        _result = "running";
        return new(Run(player, args.FirstOrDefault() ?? "preview"), true, "Shine issue check started.");
    }
    private static async Task Run(Player player, string mode)
    {
        var rows = new List<object>();
        bool passed = true;
        void Check(string name, bool pass, object? data = null) { passed &= pass; rows.Add(new { name, pass, data }); }
        CardModel Fresh(string id) => player.RunState.CreateCard(CardCatalog.Model(id), player);
        try
        {
            if (mode == "preview")
            {
                var old = Fresh("JK131"); var duplicate = Fresh("JK131");
                using (KnowledgeCombat.SingleCard()) { await CardPileCmd.Add(old, PileType.Deck); await CardPileCmd.Add(duplicate, PileType.Deck); }
                Shine.SetState(old, 1, 9); Shine.SetState(duplicate, 2, 9);
                var offer = Fresh("JK131B");
                var preview = KnowledgePreviewCards.Create(offer).Single(card => card.Id == old.Id);
                Check("reward companion displays fresh Shine 9", Shine.GetState(preview)?.Remaining == 9, Shine.GetState(preview));
                Check("snapshot preserves spent state", Shine.GetState(CardCopies.Snapshot(old))?.Remaining == 1);
                Check("deck view preserves spent companion", Shine.GetState(KnowledgePreviewCards.Create(old).First(card => card.Id == old.Id))?.Remaining == 1);
                var before = player.Deck.Cards.ToHashSet();
                await CardPileCmd.Add(offer, PileType.Deck);
                var gained = player.Deck.Cards.Where(card => !before.Contains(card) && card.Id == old.Id).ToArray();
                Check("acquisition grants one fresh companion", gained.Length == 1 && Shine.GetState(gained[0])?.Remaining == 9, gained.Select(Shine.GetState).ToArray());
                Check("same-name originals retain independent budgets", Shine.GetState(old)?.Remaining == 1 && Shine.GetState(duplicate)?.Remaining == 2);
                var entrance = Fresh("JK126");
                await DynamicKnowledge.Prepare(new ThrowingPlayerChoiceContext(), entrance, [entrance, Fresh("JK131"), Fresh("JK131")]);
                await CardPileCmd.Add(entrance, PileType.Deck);
                var repeated = KnowledgePreviewCards.Create(Fresh("JK126")).Where(card => card.Id == old.Id).ToArray();
                Check("repeated companion offers each start full", repeated.Length == 2 && repeated.All(card => Shine.GetState(card)?.Remaining == 9));
                var restored = Player.FromSerializable(player.ToSerializable());
                foreach (var savedCard in restored.Deck.Cards) savedCard.Owner = restored;
                var restoredOffer = (CardModel)CardCatalog.Model("JK126").MutableClone(); restoredOffer.Owner = restored;
                var restoredPreviews = KnowledgePreviewCards.Create(restoredOffer).Where(card => card.Id == old.Id).ToArray();
                Check("loaded relation keeps repeated fresh offer budgets", restoredPreviews.Length == 2 && restoredPreviews.All(card => Shine.GetState(card)?.Remaining == 9));
                Check("loaded same-name originals retain their budgets", restored.Deck.Cards.Where(card => card.Id == old.Id).Take(2).Select(card => Shine.GetState(card)?.Remaining).SequenceEqual(new int?[] { 1, 2 }));
            }
            else if (mode == "archive")
            {
                var card = Fresh("JK131"); card.UpgradeInternal(); Shine.SetState(card, 0, 9);
                var runtime = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name?.StartsWith("ShoujoKagekiAijoCore.Runtime.") == true);
                var archive = runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineArchive")!;
                archive.GetMethod("Restore")!.Invoke(null, [player, new[] { card.ToSerializable() }]);
                var model = Shine.GetDepletedModels(player).Last();
                string expected = new LocString("gameplay_ui", "CORE_SHINE_LABEL").GetFormattedText() + "9" + new LocString("gameplay_ui", "CORE_SHINE_SUFFIX").GetFormattedText();
                string text = model.GetDescriptionForPile(PileType.None);
                Check("depleted pile displays original Shine maximum", text.EndsWith(expected, StringComparison.Ordinal), text);
                var detail = CardCopies.Snapshot(model);
                Check("archived detail snapshot preserves display and zero budget", detail.GetDescriptionForPile(PileType.None).EndsWith(expected, StringComparison.Ordinal) && Shine.GetState(detail)?.Remaining == 0);
                Check("archive remains depleted", Shine.GetState(model) is { Remaining: 0, Temporary: false, PendingDepletion: false });
                Check("ordinary copies remain empty shells", Shine.Copy(model, CardCopyPurpose.TemporaryCopy).GetType().Name == "CoreEmptyShell" && Shine.Copy(model, CardCopyPurpose.Transfer).GetType().Name == "CoreEmptyShell" && model.MutableClone().GetType().Name == "CoreEmptyShell");
                var renewed = Shine.Reacquire(player, Shine.GetDepletedCards(player).Last());
                Check("explicit reacquisition restores full upgraded card", Shine.GetState(renewed) is { Remaining: 9, Maximum: 9, Temporary: false } && renewed.CurrentUpgradeLevel == 1);
                Shine.SetState(renewed, 2, 9);
                Check("new lifecycle uses its actual remaining display", renewed.GetDescriptionForPile(PileType.Deck).Contains("[red]2[/red]", StringComparison.Ordinal));
                // Re-run the actual old-save archive restoration entry and verify the state again.
                var snapshots = archive.GetMethod("Snapshots")!.Invoke(null, [player]);
                archive.GetMethod("Restore")!.Invoke(null, [player, snapshots]);
                Check("restored archive keeps zero budget and presentation", Shine.GetState(Shine.GetDepletedModels(player).Last())?.Remaining == 0 && Shine.GetDepletedModels(player).Last().GetDescriptionForPile(PileType.None).EndsWith(expected, StringComparison.Ordinal));
            }
            else
            {
                if (player.PlayerCombatState == null) throw new InvalidOperationException("Combat required.");
                var context = new ThrowingPlayerChoiceContext();
                foreach (var existing in ArrowCombat.Deployed.ToArray()) ArrowCombat.Cancel(existing.Card);
                var permanent = Fresh("JK133");
                using (KnowledgeCombat.SingleCard()) await CardPileCmd.Add(permanent, PileType.Deck);
                Shine.SetState(permanent, mode == "replay" ? 4 : 1, 6);
                var card = CardCopies.ForCombat(permanent, player, CardCopyPurpose.Transfer, permanent);
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
                var target = player.Creature.CombatState!.Enemies.First(enemy => enemy.IsAlive && enemy.IsHittable);
                if (mode == "selfkill") await CreatureCmd.SetCurrentHp(target, 1);
                var play = new CardPlay { Card = card, Target = target, ResultPile = PileType.Discard, Resources = default, IsAutoPlay = true, PlayIndex = 0, PlayCount = mode == "replay" ? 3 : 1,
#if STS2_BETA
                    Player = player,
#endif
                };
                int effects = 0;
                MegaCrit.Sts2.Core.Entities.Creatures.Creature? actualTarget = null;
                if (mode == "retarget" && player.Creature.CombatState!.Enemies.Count(enemy => enemy.IsAlive && enemy.IsHittable) < 2) throw new InvalidOperationException("Two enemies required for retarget check.");
                ArrowCombat.Deploy(card, play, 1, async (choice, payload) =>
                {
                    effects++;
                    actualTarget = payload.Target;
                    await CardEffects.Resolve((CatalogCard)card, choice, payload);
                });
                await CardPileCmd.Add(card, ArrowPile.PileId);
                int archives = Shine.GetDepletedCards(player).Count;
                var arrow = ArrowCombat.Find(card)!;
                var pending = ArrowCombat.Accelerate(context, arrow, 1);
                if (mode == "retarget") await CreatureCmd.Kill(target);
                if (mode == "race") await CreatureCmd.Kill(player.Creature.CombatState!.Enemies.Where(enemy => enemy.IsAlive).ToArray());
                await pending;
                if (mode == "race")
                {
                    Check("battle ending before arrow payload preserves last use", Shine.GetState(permanent)?.Remaining == 1, new { permanent = Shine.GetState(permanent), effects });
                    Check("invalid queued arrow does not enter effect", effects == 0);
                    Check("invalid queued arrow adds no depleted archive", Shine.GetDepletedCards(player).Count == archives);
                    Check("original permanent card remains in deck", player.Deck.Cards.Contains(permanent));
                }
                else if (mode == "replay")
                {
                    Check("each valid replay consumes one independent use", effects == 3 && Shine.GetState(permanent)?.Remaining == 1, new { effects, state = Shine.GetState(permanent) });
                    Check("remaining replay budget stays in deck without archive", player.Deck.Cards.Contains(permanent) && Shine.GetDepletedCards(player).Count == archives);
                }
                else
                {
                    if (mode == "retarget") Check("dead target retargets before payload", actualTarget != null && actualTarget != target && effects == 1);
                    if (mode == "selfkill") Check("own effective kill counts exactly once", !target.IsAlive && effects == 1);
                    Check("valid arrow effect executes", effects >= 1, effects);
                    Check("valid last use depletes permanent card once", Shine.GetDepletedCards(player).Count == archives + 1 && !player.Deck.Cards.Contains(permanent), Shine.GetState(permanent));
                }
            }
        }
        catch (Exception error) { passed = false; rows.Add(new { error = error.ToString() }); }
        _result = JsonSerializer.Serialize(new { mode, passed, rows });
        var directory = Godot.ProjectSettings.GlobalizePath("user://shine-issues"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, mode + ".json"), _result);
    }
}
