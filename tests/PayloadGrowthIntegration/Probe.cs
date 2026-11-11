using System.Reflection;
using System.Text.Json;
using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.addons.mega_text;
using ShoujoKageki.Core.Exports;
using ShoujoKageki.Core.Runtime.Modules.Execution;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;

namespace PayloadGrowthIntegration;

[ModInitializer(nameof(Initialize))]
public static class Startup
{
    public static void Initialize() => new Harmony("CorePayloadGrowthIntegration").PatchAll();
}

[Pool(typeof(ColorlessCardPool))]
public sealed class PayloadOwnerProbe() : CustomCardModel(0, CardType.Skill, CardRarity.Token, TargetType.Self, showInCardLibrary: false)
{
    public static Player? SeenOwner;
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play) { SeenOwner = Owner; return Task.CompletedTask; }
}

internal static class ExportTrace
{
    internal static Type Session => typeof(CardPngExportUtil).Assembly.GetType("ShoujoKageki.Core.Exports.CardExportSession", true)!;
    internal static bool FailDuringBuild;
    internal static List<Node> Nodes = [];
    internal static List<CardModel> Models = [];
    internal static List<(string Id, string Title, string Expected)> Titles = [];
    internal static void Observe(object session)
    {
        var roots = (IEnumerable<Node>)AccessTools.Field(Session, "_nodes").GetValue(session)!;
        var all = new HashSet<Node>();
        void Add(Node node)
        {
            if (!all.Add(node)) return;
            foreach (var child in node.GetChildren()) Add(child);
        }
        foreach (var node in roots) Add(node);
        Nodes = all.ToList();
        Titles.Clear();
        Models = ((IEnumerable<CardModel>)AccessTools.Field(Session, "_models").GetValue(session)!).ToList();
    }
}
[HarmonyPatch]
internal static class ObserveBuiltExport
{
    private static MethodBase TargetMethod() => AccessTools.Method(ExportTrace.Session, "BuildViewport");
    private static void Postfix(object __instance) => ExportTrace.Observe(__instance);
}
[HarmonyPatch]
internal static class ObserveRenderedCard
{
    private static MethodBase TargetMethod() => AccessTools.Method(ExportTrace.Session, "RefreshCardVisuals");
    private static void Postfix(NCard nCard)
        => ExportTrace.Titles.Add((nCard.Model!.Id.ToString(), nCard.GetNode<MegaLabel>("%TitleLabel").Text, nCard.Model.Title));
}
[HarmonyPatch]
internal static class FailPartialExport
{
    private static MethodBase TargetMethod() => AccessTools.Method(ExportTrace.Session, "PopulateHoverTips");
    private static void Prefix(object __instance)
    {
        if (!ExportTrace.FailDuringBuild) return;
        ExportTrace.Observe(__instance);
        throw new IOException("expected-partial-export-failure");
    }
}

public sealed class PayloadGrowthFixture : AbstractConsoleCmd
{
    private static string Report = "{}";
    public override string CmdName => "payload_growth_fixture";
    public override string Args => "run|report";
    public override string Description => "Directed payload, research save and export lifetime checks.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args) => args.FirstOrDefault() == "report"
        ? new(true, Report) : player?.PlayerCombatState == null ? new(false, "Combat required")
        : new(Run(player), true, "running");

    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        Report = JsonSerializer.Serialize(new { status = "running" });
        void Check(string label, bool passed)
        {
            rows.Add(new { name = label, passed });
            if (!passed) throw new InvalidOperationException(label);
        }
        var context = new ThrowingPlayerChoiceContext();
        var combat = player.Creature.CombatState!;
        var directory = System.Environment.GetEnvironmentVariable("CORE_LIFECYCLE_OUTPUT")
            ?? ProjectSettings.GlobalizePath("user://payload-growth-export");
        Directory.CreateDirectory(directory);
        try
        {
            using var single = KnowledgeCombat.SingleCard();
            foreach (var card in player.PlayerCombatState!.AllCards.ToArray())
            {
                ArrowCombat.Cancel(card);
                await CardPileCmd.RemoveFromCombat(card);
            }
            var enemy = combat.HittableEnemies.First(target => target.IsAlive);
            await CreatureCmd.SetMaxHp(enemy, 10000);
            await CreatureCmd.SetCurrentHp(enemy, 10000);
            var arrow = CardCatalog.Create("JA38", player);
            Shine.SetState(arrow, 3, 3, temporary: true);
            DamageModifiers.Add(arrow, 7);
            var observed = new List<(CardModel Card, PlayerChoiceContext Context)>();
            using (CoreApi.Get<ICardEffectRouting>().Register(async (card, choice, play, next) =>
            {
                observed.Add((card, choice));
                await next(choice, play);
            }, _ => null))
            {
                await CardPileCmd.Add(arrow, PileType.Hand);
                await CardExecution.Play(context, arrow, new CardExecutionRequest { Target = enemy });
                Check("deployment retains downstream middleware", observed.Count == 0);
                var original = ArrowCombat.Find(arrow)!;
                typeof(ArrowDeployment).GetProperty(nameof(ArrowDeployment.PlayCount))!.SetValue(original, 2);
                await ArrowCombat.CopyAll(context, player);
                var copied = ArrowCombat.Deployed.Single(deployment => deployment.Card != arrow);
                Check("copy retains delay target and replay count", copied.Remaining == original.Remaining
                    && copied.Target == original.Target && copied.PlayCount == 2);
                Check("copy retains local investment", DamageModifiers.CaptureDamageModifierSnapshot(copied.Card).DamageBonus == 7);
                var resumed = new ThrowingPlayerChoiceContext();
                int hp = enemy.CurrentHp;
                await ArrowCombat.Accelerate(resumed, copied, 99);
                Check("copy traverses downstream middleware for every payload", observed.Count == 2
                    && observed.All(item => item.Card == copied.Card && item.Context == resumed));
                Check("copy damages and debits independently", enemy.CurrentHp < hp
                    && Shine.GetState(copied.Card)?.Remaining == 1 && Shine.GetState(arrow)?.Remaining == 3
                    && ArrowCombat.Find(arrow) == original);
            }
            ArrowCombat.Cancel(arrow);
            await CardPileCmd.RemoveFromCombat(arrow);

            var ownerProbe = combat.CreateCard<PayloadOwnerProbe>(player);
            var other = Player.FromSerializable(player.ToSerializable());
            foreach (var card in other.Deck.Cards) card.Owner = other;
            var foreign = CardOptions.Create(ownerProbe, other);
            CardPlay Play(CardModel card) => new()
            {
                Card = card, Target = null, ResultPile = PileType.Discard,
                Resources = default, IsAutoPlay = true, PlayIndex = 0, PlayCount = 1,
#if STS2_BETA
                Player = card.Owner,
#endif
            };
            ICardEffectPayload? deferred = null;
            using (CoreApi.Get<ICardPayloadRouting>().Register((card, choice, play, next) =>
            { deferred = next; return Task.CompletedTask; }, _ => null))
            {
                Player? hookOwner = null;
                Func<CardModel, PlayerChoiceContext, CardPlay, Task> nativeHook = (card, choice, play) =>
                { hookOwner = card.Owner; return Task.CompletedTask; };
                await CardPlayRoutingModule.DispatchFromHook(nativeHook, ownerProbe, context, Play(ownerProbe));
                await deferred!.Bind(foreign)(context, Play(foreign));
                Check("third-party delegate binds to a different recipient", hookOwner == other && !ReferenceEquals(other, player));
            }
            await CoreApi.Get<ICardPayloadRouting>().Native.Bind(foreign)(context, Play(foreign));
            Check("native payload binds to a different recipient", PayloadOwnerProbe.SeenOwner == other);

            SavedSpireField<CatalogCard, T> Field<T>(string name)
                => (SavedSpireField<CatalogCard, T>)AccessTools.Field(typeof(KnowledgeProgress), name).GetValue(null)!;
            var links = Field<string>("ResearchLinks");
            var completed = Field<bool>("ResearchCompleted");
            var hits = Field<int>("ExtraHits");
            var legacyDepleted = Field<int>("LegacyDepletedPartners");
            void Bind(IEnumerable<CardModel> cards) => AccessTools.Method(typeof(KnowledgeProgress), "BindResearch").Invoke(null, [cards]);
            async Task<CatalogCard> Deck(string id)
            {
                var card = (CatalogCard)player.RunState.CreateCard(CardCatalog.Model(id), player);
                await CardPileCmd.Add(card, PileType.Deck);
                return card;
            }
            foreach (var (mainId, memberIds, maximum) in new[]
            {
                ("JK148", new[] { "JK148B", "JK148C", "JK148D", "JK148E", "JK148F" }, (int?)null),
                ("JK184", new[] { "JK184B", "JK184C", "JK184D" }, (int?)6)
            })
            {
                var main = await Deck(mainId);
                var members = new List<CatalogCard>();
                foreach (string id in memberIds) members.Add(await Deck(id));
                Bind(members.Prepend<CardModel>(main));
                var isolated = await Deck(mainId);
                var isolatedMember = await Deck(memberIds[0]);
                Bind(new CardModel[] { isolated, isolatedMember });
                Check(mainId + " acquisition links only its own batch", members.All(card => links.Get(card) == links.Get(main))
                    && links.Get(isolated) != links.Get(main));
                var inCombat = (CatalogCard)CardCopies.ForCombat(main, player, CardCopyPurpose.Transfer, main);
                await CardPileCmd.Add(inCombat, PileType.Hand);
                async Task Deplete(CatalogCard member)
                    => await KnowledgeProgress.Depleted(member, new ShineDepletionContext(member, member, player, "fixture")
                    { ChoiceContext = context, CombatState = combat });
                await Deplete(members[0]);
                Check(mainId + " updates permanent and live copies", KnowledgeProgress.PermanentHits(main) == 1
                    && KnowledgeProgress.PermanentHits(inCombat) == 1 && KnowledgeProgress.PermanentHits(isolated) == 0);
                await Deplete(members[0]);
                Check(mainId + " counts a physical partner once", KnowledgeProgress.PermanentHits(main) == 1);
                var restoredMain = (CatalogCard)CardModel.FromSerializable(main.ToSerializable()); restoredMain.Owner = player;
                var restoredMember = (CatalogCard)CardModel.FromSerializable(members[0].ToSerializable()); restoredMember.Owner = player;
                Check(mainId + " restores original progress and link fields", KnowledgeProgress.PermanentHits(restoredMain) == 1
                    && links.Get(restoredMain) == links.Get(main) && completed.Get(restoredMember));
                await Deplete(restoredMember);
                Check(mainId + " restored completed partner cannot advance again", KnowledgeProgress.PermanentHits(main) == 1);
                hits.Set(main, maximum is { } cap ? cap - 1 : 7);
                await Deplete(members[1]);
                await Deplete(members[2]);
                int expected = maximum ?? 9;
                Check(mainId + " keeps its original cap and live projection", KnowledgeProgress.PermanentHits(main) == expected
                    && KnowledgeProgress.PermanentHits(inCombat) == expected);
                Check(mainId + " keeps its description counter", KnowledgeProgress.DepletedPartners(main) == (mainId == "JK148" ? 0 : expected));
            }
            var archiveMain = await Deck("JK314");
            var firstDesire = await Deck("JK122B");
            Bind(new CardModel[] { archiveMain, firstDesire });
            Check("archive research does not acquire a batch identity",
                string.IsNullOrEmpty(links.Get(archiveMain)) && string.IsNullOrEmpty(links.Get(firstDesire)));
            var archiveLive = (CatalogCard)CardCopies.ForCombat(archiveMain, player, CardCopyPurpose.Transfer, archiveMain);
            await CardPileCmd.Add(archiveLive, PileType.Hand);
            int archivedBefore = Shine.GetDepletedCards(player).Count(entry => entry.ModelId == firstDesire.Id.ToString());
            legacyDepleted.Set(archiveMain, 999);
            Check("archive research ignores old counters and canonical cards have no owner count",
                KnowledgeProgress.DepletedPartners(archiveMain) == archivedBefore
                && KnowledgeProgress.DepletedPartners(CardCatalog.Model("JK314")) == 0);
            for (int i = 0; i < 4; i++)
            {
                var permanent = i == 0 ? firstDesire : await Deck("JK122B");
                Shine.SetState(permanent, 1, 3);
                var desire = (CatalogCard)CardCopies.ForCombat(permanent, player, CardCopyPurpose.Transfer, permanent);
                Shine.SetState(desire, 1, 3);
                await CardPileCmd.Add(desire, PileType.Hand);
                await CardExecution.Play(context, desire, new CardExecutionRequest { Target = enemy });
                Check("archive research counts native depletion across batches " + i,
                    KnowledgeProgress.DepletedPartners(archiveMain) == archivedBefore + i + 1
                    && KnowledgeProgress.DepletedPartners(archiveLive) == archivedBefore + i + 1);
                await KnowledgeProgress.Depleted(desire, new ShineDepletionContext(desire, permanent, player, "repeat"));
                Check("repeat callbacks do not duplicate archive progress " + i,
                    KnowledgeProgress.DepletedPartners(archiveLive) == archivedBefore + i + 1);
            }
            var restoredArchiveMain = (CatalogCard)CardModel.FromSerializable(archiveMain.ToSerializable());
            restoredArchiveMain.Owner = player;
            Check("legacy field roundtrips while archive progress exceeds three",
                legacyDepleted.Get(restoredArchiveMain) == 999
                && KnowledgeProgress.DepletedPartners(restoredArchiveMain) == archivedBefore + 4);
            Check("archive hits remain separate from permanent and invested hits",
                KnowledgeProgress.PermanentHits(archiveLive) == 0
                && DamageModifiers.CaptureDamageModifierSnapshot(archiveLive).ExtraHits == 0);
            DamageModifiers.Add(archiveLive, extraHits: 1);
            int archiveHp = enemy.CurrentHp;
            await CardExecution.Play(context, archiveLive, new CardExecutionRequest { Target = enemy });
            Check("archive hits combine with external investment during native play",
                archiveHp - enemy.CurrentHp == 8 * (2 + archivedBefore + 4 + 1));

            var unbound = await Deck("JK148B");
            Bind(new[] { unbound });
            Check("partner-only acquisition cannot start a research batch", string.IsNullOrEmpty(links.Get(unbound)));

            var exportCard = (CatalogCard)CardCatalog.Create("JK148", player);
            await CardPileCmd.Add(exportCard, PileType.Hand);
            Shine.SetState(exportCard, 2, 5, temporary: true);
            string originalSave = JsonSerializer.Serialize(exportCard.ToSerializable());
            int combatCount = player.PlayerCombatState.AllCards.Count();
            int HostCount() => NGame.Instance!.GetTree().Root.FindChildren("CardExportHost", "", true, false).Count;
            int hosts = HostCount();
            void Released(string label) => Check(label,
                ExportTrace.Nodes.Count > 0 && ExportTrace.Nodes.All(node => !GodotObject.IsInstanceValid(node)) && HostCount() == hosts);
            void Pixels(string file, bool tips)
            {
                using var image = Image.LoadFromFile(Path.Combine(directory, file));
                int visible = 0;
                for (int y = 0; y < image.GetHeight(); y += Math.Max(1, image.GetHeight() / 32))
                    for (int x = 0; x < image.GetWidth(); x += Math.Max(1, image.GetWidth() / 32))
                        if (image.GetPixel(x, y).A > .1f) visible++;
                Check(file + " has visible content", !image.IsEmpty() && !image.IsInvisible() && visible > 50);
                Check(file + " preserves card dimensions", tips ? image.GetWidth() > Mathf.CeilToInt(380f * .7f) + 12
                    : image.GetWidth() == Mathf.CeilToInt(380f * .7f) + 12 && image.GetHeight() == Mathf.CeilToInt(480f * .7f) + 12);
            }
            Check("plain image exports", await CardPngExportUtil.ExportSingleAsync(exportCard, Path.Combine(directory, "plain.png"), .7f, false));
            Released("success releases all capture nodes"); Pixels("plain.png", false);
            Check("export snapshots stay outside combat registration", ExportTrace.Models.All(card => card.Pile == null && !combat.ContainsCard(card)));
            Check("canonical card and hover references export", await CardPngExportUtil.ExportSingleAsync(CardCatalog.Model("JA145"), Path.Combine(directory, "hover.png"), .7f, true));
            Released("reference export releases all capture nodes"); Pixels("hover.png", true);
            Check("main and reference cards render their actual titles",
                ExportTrace.Titles.Count == 2 && ExportTrace.Titles.All(item => item.Title == item.Expected)
                && ExportTrace.Titles.Select(item => item.Id).ToHashSet().SetEquals(
                    new[] { CardCatalog.Model("JA145").Id.ToString(), CardCatalog.Model("JA119").Id.ToString() }));
            ExportTrace.FailDuringBuild = true;
            bool failed = !await CardPngExportUtil.ExportSingleAsync(exportCard, Path.Combine(directory, "partial.png"), .7f, true);
            ExportTrace.FailDuringBuild = false;
            Check("partial construction fails before attaching to the scene", failed && ExportTrace.Nodes.Count > 10
                && ExportTrace.Nodes.Any(node => node is SubViewport));
            Released("partial construction releases detached nodes and viewport");
            string blocked = Path.Combine(directory, "blocked"); Directory.CreateDirectory(blocked);
            Check("file failure is reported", !await CardPngExportUtil.ExportSingleAsync(exportCard, blocked, .7f, false));
            Released("file failure releases all capture nodes");
            Check("capture leaves the actual card and combat state intact",
                originalSave == JsonSerializer.Serialize(exportCard.ToSerializable())
                && player.PlayerCombatState.AllCards.Count() == combatCount
                && exportCard.Pile == player.PlayerCombatState.Hand && Shine.GetState(exportCard)?.Remaining == 2);
            Report = JsonSerializer.Serialize(new { status = "done", passed = true, rows });
        }
        catch (Exception error)
        {
            ExportTrace.FailDuringBuild = false;
            Report = JsonSerializer.Serialize(new { status = "done", passed = false, rows, error = error.ToString() });
        }
    }
}
