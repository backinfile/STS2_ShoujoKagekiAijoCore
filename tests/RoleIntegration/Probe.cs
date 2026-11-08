using System.Text.Json;
using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.CardPools;
using BaseLib.Utils.Attributes;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;

namespace CoreRoleIntegration;

[CustomID("CORE_ROLE_PROBE")]
[ShineCard(3)]
[Pool(typeof(ColorlessCardPool))]
public sealed class ResolutionProbeCard() : CustomCardModel(0, CardType.Skill, CardRarity.Token, TargetType.Self, showInCardLibrary: false), IShineResolutionHandler
{
    public static int Effects;
    public static readonly List<int> ResolutionIndices = [];
    public override string CustomPortraitPath => CoreApi.ResourceRoot + "images/cards/empty_shell.png";
    public override CardPoolModel VisualCardPool => ModelDb.CardPool<ColorlessCardPool>();
    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        Effects++;
        await CreatureCmd.GainBlock(Owner.Creature, 1, ValueProp.Move, play);
    }
    public Task AfterShineResolution(PlayerChoiceContext context, CardPlay play)
    {
        ResolutionIndices.Add(play.PlayIndex);
        return Task.CompletedTask;
    }
}

public sealed class CoreRoleCommand : AbstractConsoleCmd
{
    private static string _result = "No checks have run.";
    public override string CmdName => "core_roles";
    public override string Args => "run | status | config | archive | error | export-one";
    public override string Description => "Targeted Core and Junna adoption checks in an isolated game.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? player, string[] args)
    {
        if (args.FirstOrDefault() == "status") return new(true, _result);
        if (args.FirstOrDefault() == "error")
        {
            Godot.GD.PushError("Core role integration probe error");
            return new(true, "Submitted a controlled engine error to the normal logger.");
        }
        if (args.FirstOrDefault() == "config")
            return new(true, JsonSerializer.Serialize(new
            {
                Settings = CoreApi.Configuration.Current,
                ApiAssemblies = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "ShoujoKagekiAijoCore.Api.V1"),
                Runtimes = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name)
                    .Where(name => name?.StartsWith("ShoujoKagekiAijoCore.Runtime.", StringComparison.Ordinal) == true).ToArray(),
                Diagnostics = CoreApi.Get<ICoreDiagnostics>().Snapshot(),
                ModelSources = CoreApi.Get<ICoreDiscovery>().Models.GroupBy(model => model.ModId)
                    .Select(group => new { ModId = group.Key, Count = group.Count() }).ToArray(),
                Deck = player?.Deck.Cards.Select(card => new { Id = card.Id.ToString(), State = Shine.GetState(card) }).ToArray(),
                Depleted = player == null ? null : Shine.GetDepletedCards(player),
                ArchivedStates = player == null ? null : Shine.GetDepletedModels(player)
                    .Select(card => new { Id = card.Id.ToString(), State = Shine.GetState(card) }).ToArray(),
                Rules = SavedRules(player),
                SharedPatchOwners = new[] { typeof(BookOfFiveRings), typeof(LuckyFysh) }.Select(type => new
                {
                    Model = type.Name,
                    Owners = Harmony.GetPatchInfo(AccessTools.Method(type, "AfterCardChangedPiles"))?.Owners.ToArray() ?? []
                }).ToArray()
            }));
        if (args.FirstOrDefault() == "export-one") return new(ExportOne(), true, "Single-card export started.");
        if (args.FirstOrDefault() == "archive" && player != null) return new(true, CheckArchive(player));
        if (player?.PlayerCombatState == null) return new(false, "Combat required.");
        return new(Run(player), true, "Core role checks started.");
    }

    internal static string LastResult => _result;

    private static object? SavedRules(Player? player)
    {
        if (player == null) return null;
        var runtime = AppDomain.CurrentDomain.GetAssemblies().Single(assembly =>
            assembly.GetName().Name?.StartsWith("ShoujoKagekiAijoCore.Runtime.", StringComparison.Ordinal) == true);
        return runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineArchive", throwOnError: true)!
            .GetMethod("GetRules")!.Invoke(null, [player]);
    }

    private static string CheckArchive(Player player)
    {
        var entries = Shine.GetDepletedCards(player);
        var models = Shine.GetDepletedModels(player);
        var rows = models.Select((card, index) =>
        {
            var copy = Shine.Copy(card, CardCopyPurpose.TemporaryCopy);
            var transfer = Shine.Copy(card, CardCopyPurpose.Transfer);
            var clone = (CardModel)card.MutableClone();
            var reacquired = Shine.Reacquire(player, entries[index]);
            var state = Shine.GetState(reacquired);
            bool pass = Shine.GetState(card) is { Remaining: 0, Temporary: false }
                && copy.GetType().Name == "CoreEmptyShell" && transfer.Id == copy.Id && clone.Id == copy.Id
                && state is { Temporary: false, PendingDepletion: false } && state.Remaining == state.Maximum
                && state.Maximum == entries[index].Maximum && reacquired.CurrentUpgradeLevel == card.CurrentUpgradeLevel;
            return new { id = card.Id.ToString(), pass, archived = Shine.GetState(card), reacquired = state };
        }).ToArray();
        return JsonSerializer.Serialize(new { passed = entries.Count == models.Count && rows.All(row => row.pass), rows });
    }

    private static async Task ExportOne()
    {
        try
        {
            var runtime = AppDomain.CurrentDomain.GetAssemblies().Single(assembly =>
                assembly.GetName().Name?.StartsWith("ShoujoKagekiAijoCore.Runtime.", StringComparison.Ordinal) == true);
            var export = runtime.GetType("ShoujoKageki.Core.Exports.CardPngExportUtil", throwOnError: true)!
                .GetMethod("ExportSingleAsync")!;
            var model = CoreApi.Get<ICoreDiscovery>().Models.First(item => item.ModelType.Name == "KarenShineStrike");
            var card = ModelDb.GetById<CardModel>(ModelDb.GetId(model.ModelType));
            string directory = Godot.ProjectSettings.GlobalizePath("user://core-role-integration");
            Directory.CreateDirectory(directory);
            var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
            object NodeCounts() => new
            {
                Hosts = tree.Root.FindChildren("CardExportHost", "", true, false).Count,
                SubViewports = tree.Root.FindChildren("*", "SubViewport", true, false).Count
            };
            var before = NodeCounts();
            var task = (Task<bool>)export.Invoke(null, [card, Path.Combine(directory, "karen-shine-strike.png"), 1f, true])!;
            bool exported = await task;
            await tree.ToSignal(tree, Godot.SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, Godot.SceneTree.SignalName.ProcessFrame);
            _result = JsonSerializer.Serialize(new { exported, before, after = NodeCounts() });
        }
        catch (Exception error) { _result = JsonSerializer.Serialize(new { exported = false, error = error.ToString() }); }
    }

    internal static async Task Run(Player player)
    {
        var rows = new List<object>();
        var context = new ThrowingPlayerChoiceContext();
        var combat = player.Creature.CombatState!;
        void Check(string name, bool pass)
        {
            rows.Add(new { name, pass });
            if (!pass) throw new InvalidOperationException(name);
        }
        async Task<CardModel> Card()
        {
            var card = combat.CreateCard(ModelDb.Card<ResolutionProbeCard>(), player);
            await CardPileCmd.Add(card, PileType.Hand, skipVisuals: false);
            return card;
        }
        try
        {
            foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
            foreach (var card in player.PlayerCombatState!.AllCards.ToArray())
            {
                ArrowCombat.Cancel(card);
                await CardPileCmd.RemoveFromCombat(card, skipVisuals: false);
            }
            var native = await Card();
            ResolutionProbeCard.Effects = 0;
            ResolutionProbeCard.ResolutionIndices.Clear();
            await CardCmd.AutoPlay(context, native, player.Creature);
            Check("native play consumes one use", Shine.GetState(native)?.Remaining == 2);
            Check("ordinary exhaust preserves the remaining budget", native.Pile?.Type == PileType.Exhaust && Shine.GetState(native)?.Remaining == 2);
            Check("native post-resolution callback runs once", ResolutionProbeCard.ResolutionIndices.SequenceEqual([0]));
            native.UpgradeInternal();
            native.FinalizeUpgradeInternal();
            Check("upgrade restores the shine maximum", Shine.GetState(native)?.Remaining == 3);

            var arrow = await Card();
            Shine.GrantTemporary(arrow, 1);
            CardReplays.SetNext(arrow, 2);
            ResolutionProbeCard.Effects = 0;
            ResolutionProbeCard.ResolutionIndices.Clear();
            await CardPileCmd.Add(arrow, PileType.Play, skipVisuals: false);
            await HandArrow.Play(context, arrow);
            Check("deployment preserves the budget", Shine.GetState(arrow)?.Remaining == 1);
            Check("deployment skips actual-effect callbacks", ResolutionProbeCard.Effects == 0 && ResolutionProbeCard.ResolutionIndices.Count == 0);
            var deployment = ArrowCombat.Find(arrow) ?? throw new InvalidOperationException("Arrow did not deploy.");
            Check("native replay budgeting records three payloads", deployment.PlayCount == 3 && CardReplays.GetNext(arrow) == 0);
            await ArrowCombat.Accelerate(context, deployment, 100);
            Check("all recorded payloads finish before depletion", ResolutionProbeCard.Effects == 3);
            Check("each actual replay reports its own index", ResolutionProbeCard.ResolutionIndices.SequenceEqual([0, 1, 2]));
            Check("the completed batch clamps the budget to zero", Shine.GetState(arrow)?.Remaining == 0);
            Check("the completed deployment is removed", ArrowCombat.Find(arrow) == null);

            var ordinary = CardCatalog.Create("JB01", player);
            Check("an ordinary card has no shine state", Shine.GetState(ordinary) == null);
            var ordinaryCopy = Shine.Copy(ordinary, CardCopyPurpose.TemporaryCopy);
            Check("ordinary copying preserves its model and absent shine state", ordinaryCopy.Id == ordinary.Id && Shine.GetState(ordinaryCopy) == null);

            var source = await Card();
            Shine.SetState(source, 2, 3, temporary: true);
            var copy = Shine.Copy(source, CardCopyPurpose.TemporaryCopy);
            await Shine.ResolveAsync(copy, () => Task.CompletedTask, CardResolutionKind.Arrow);
            Check("a temporary copy has an independent budget", Shine.GetState(copy)?.Remaining == 1 && Shine.GetState(source)?.Remaining == 2);
            var effectPlay = new CardPlay
            {
                Card = source, Target = player.Creature, ResultPile = PileType.Discard,
#if STS2_BETA
                Player = player,
#endif
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = true, PlayIndex = 0, PlayCount = 1
            };
            await Shine.ResolveAsync(source, () => (Task)typeof(ResolutionProbeCard)
                .GetMethod("OnPlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(source, [context, effectPlay])!, CardResolutionKind.Effect);
            Check("an extra payload does not consume a use", Shine.GetState(source)?.Remaining == 2);
            Shine.SetState(source, 0, 3, temporary: true);
            var shell = Shine.Copy(source, CardCopyPurpose.TemporaryCopy);
            Check("zero-use copying produces the common shell", shell.Id != source.Id && shell.Type == CardType.Skill && shell.Rarity == CardRarity.Token && shell.Keywords.Contains(CardKeyword.Exhaust));
            var transfer = Shine.Copy(source, CardCopyPurpose.Transfer);
            Check("zero-use transfer produces the common shell", transfer.Id == shell.Id);
            var rawClone = (CardModel)source.MutableClone();
            Check("native zero-use copying produces the common shell", rawClone.Id == shell.Id);
            var legacyShell = shell.ToSerializable();
            legacyShell.Id = new ModelId("CARD", "KAREN_EMPTY_SHELL");
            Check("the legacy shell ID restores the shared model", CardModel.FromSerializable(legacyShell).Id == shell.Id);
            var reacquired = Shine.Copy(source, CardCopyPurpose.Reacquire);
            Check("explicit reacquisition starts a full new budget", Shine.GetState(reacquired) is { Remaining: 3, Temporary: false });
            _result = JsonSerializer.Serialize(new { passed = true, rows });
        }
        catch (Exception error)
        {
            _result = JsonSerializer.Serialize(new { passed = false, rows, error = error.ToString() });
        }
        var directory = Godot.ProjectSettings.GlobalizePath("user://core-role-integration");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "results.json"), _result);
    }
}
