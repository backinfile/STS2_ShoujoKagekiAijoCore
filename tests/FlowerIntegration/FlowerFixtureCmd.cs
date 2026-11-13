using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ShoujoKageki.Core.Runtime.Modules.Relics;

// Test-only console commands, excluded from the release assembly.
public sealed class FlowerFixtureCmd : AbstractConsoleCmd
{
    public override string CmdName => "flower_fixture";
    public override string Args => "shared-pool|audit|prepare|reward-check|claim-check|chest-one|chest-claim|chest-skip|empty|empty-setup";
    public override string Description => "Targeted Core Flower reward and unchanged treasure regression.";
    public override bool IsNetworked => true;
    internal static string Status = "idle";
    private static RelicReward? _reward;
    private static HashSet<ModelId> _before = [];
    private static HashSet<ModelId> _choices = [];
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        System.Console.WriteLine("[Flower fixture] PASS " + name);
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    public override CmdResult Process(Player? player, string[] args)
    {
        if (player == null || args.Length != 1) return new(false, "Expected one fixture name.");
        Status = "running:" + args[0];
        return new(Run(player, args[0]), true, Status);
    }
    private static async Task Run(Player p, string action)
    {
        try
        {
            if (action == "shared-pool")
            {
                var flower = ModelDb.Relic<KarenFlowerRelic>();
                Check(flower.Pool is SharedRelicPool &&
                    ModelDb.RelicPool<SharedRelicPool>().AllRelics.Count(r => r.Id == flower.Id) == 1,
                    "Flower belongs to the shared pool exactly once");
                Check(p.RelicGrabBag.ToSerializable().RelicIdLists[flower.Rarity].Contains(flower.Id),
                    "Flower is naturally obtainable by " + p.Character.Id.Entry);
                Check(flower.DynamicDescription.GetFormattedText() == "遗物奖励拥有额外一个选项。" &&
                    flower.Flavor.GetFormattedText() == "我在世界上最闪耀的样子，会第一个让你看到。",
                    "original Flower description and TV6 Kaoruko flavor load from the shipped pack");
            }
            else if (action == "audit")
            {
                var flower = p.Relics.OfType<KarenFlowerRelic>().Single();
                Check(ModelDb.RelicPool<SharedRelicPool>().AllRelics.Count(r => r is KarenFlowerRelic) == 1, "shared pool contains exactly one Flower");
                Check(flower.Icon != null && flower.IconOutline != null && flower.BigIcon != null, "native textures load");
                Check(flower.Title.GetFormattedText() == "花朵" && !flower.DynamicDescription.GetFormattedText().Contains("KAREN_FLOWER"), "localized title and description");
                Check(RelicModel.FromSerializable(flower.ToSerializable()).Id == flower.Id, "relic save identity");
                var room = p.RunState.CurrentRoom as CombatRoom ?? throw new Exception("Need a combat");
                RelicReward Make<T>() where T : RelicModel => new(ModelDb.Relic<T>().ToMutable(), p);
                foreach (var original in new[] {Make<Vajra>(), Make<OrnamentalFan>(), Make<PrayerWheel>()})
                {
                    var set = new RewardsSet(p).EmptyForRoom(room).WithCustomRewards([original]);
                    await set.GenerateWithoutOffering();
                    var linked = set.Rewards.Single() as LinkedRewardSet;
                    Check(linked?.Rewards.Count == 2, "combat relic becomes two linked choices: " + original.Relic!.Rarity);
                    var options = linked!.Rewards.Cast<RelicReward>().ToList();
                    Check(options[0] == original && options.All(r => r.Relic!.Rarity == original.Relic!.Rarity) && options[0].Relic!.Id != options[1].Relic!.Id, "preserve original, match rarity, distinct candidates");
                    await set.GenerateWithoutOffering();
                    Check(set.Rewards.Single() == linked && linked.Rewards.Count == 2, "generation is idempotent");
                }
                var first = Make<Vajra>(); var second = Make<Anchor>();
                var several = new RewardsSet(p).EmptyForRoom(room).WithCustomRewards([first, second]);
                await several.GenerateWithoutOffering();
                var groups = several.Rewards.Cast<LinkedRewardSet>().ToList();
                Check(groups.Count == 2 && groups.SelectMany(g => g.Rewards).Cast<RelicReward>().Select(r => r.Relic!.Id).Distinct().Count() == 4, "each drop gets its own distinct linked pair");
                var existing = new LinkedRewardSet([Make<Vajra>(), new GoldReward(1, p)], p);
                var special = Make<SilverCrucible>();
                var excluded = new RewardsSet(p).EmptyForRoom(room).WithCustomRewards([existing, special]);
                await excluded.GenerateWithoutOffering();
                Check(excluded.Rewards.Contains(existing) && existing.Rewards.Count == 2 && excluded.Rewards.Contains(special), "existing chain and Ancient reward stay unchanged");
                var eventReward = Make<Vajra>();
                var custom = new RewardsSet(p).WithCustomRewards([eventReward]);
                await custom.GenerateWithoutOffering();
                Check(custom.Rewards.Single() == eventReward && eventReward.ParentRewardSet == null, "non-combat custom reward stays unchanged");
                var savedBag = p.RelicGrabBag.ToSerializable();
                try
                {
                    var emptyCommon = p.RelicGrabBag.ToSerializable();
                    emptyCommon.RelicIdLists[MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Common].Clear();
                    p.RelicGrabBag.LoadFromSerializable(emptyCommon);
                    var beforeHigher = p.RelicGrabBag.ToSerializable().RelicIdLists
                        .Where(pair => pair.Key != MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Common)
                        .SelectMany(pair => pair.Value).ToList();
                    var exhausted = Make<Vajra>();
                    var exhaustedSet = new RewardsSet(p).EmptyForRoom(room).WithCustomRewards([exhausted]);
                    await exhaustedSet.GenerateWithoutOffering();
                    Check(exhaustedSet.Rewards.Single() == exhausted, "exhausted rarity keeps original reward");
                    Check(beforeHigher.SequenceEqual(p.RelicGrabBag.ToSerializable().RelicIdLists
                        .Where(pair => pair.Key != MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Common)
                        .SelectMany(pair => pair.Value)), "exhausted rarity does not consume higher rarity relics");
                }
                finally { p.RelicGrabBag.LoadFromSerializable(savedBag); }
            }
            else if (action == "prepare")
            {
                _reward = new RelicReward(ModelDb.Relic<Vajra>().ToMutable(), p);
                ((CombatRoom)p.RunState.CurrentRoom!).AddExtraReward(p, _reward);
            }
            else if (action == "reward-check")
            {
                Check(_reward?.ParentRewardSet?.Rewards.Count == 2, "real post-combat extra reward is linked");
                var group = Descendants(NGame.Instance!).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NLinkedRewardSet>()
                    .Single(node => node.LinkedRewardSet == _reward!.ParentRewardSet);
                var connector = group.GetNode<Control>("%ChainContainer").GetChildren().OfType<TextureRect>().Single();
                var rows = Descendants(group).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton>()
                    .OrderBy(button => button.GlobalPosition.Y).Select(button => button.GetGlobalRect()).ToList();
                var gapCenter = new Vector2((rows[0].GetCenter().X + rows[1].GetCenter().X) * 0.5f,
                    (rows[0].End.Y + rows[1].Position.Y) * 0.5f);
                Check(connector.GetGlobalRect().GetCenter().DistanceTo(gapCenter) < 0.1f,
                    "floral chain centers in actual button gap: actual=" +
                    connector.GetGlobalRect().GetCenter() + " expected=" + gapCenter);
                // Compare painted content with opaque sprite pixels, not full row Controls
                // or the transparent corners of the diagonal chain image.
                var textBounds = Descendants(group).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton>()
                    .Select(button =>
                    {
                        var label = button.GetNode<RichTextLabel>("%Label");
                        return label.GetGlobalTransform() * label.GetVisibleContentRect().Grow(6);
                    }).ToList();
                using var chainImage = ((TextureRect)connector).Texture.GetImage();
                var rect = connector.GetGlobalRect();
                var clear = true;
                for (var y = 0; y < chainImage.GetHeight(); y += 4)
                for (var x = 0; x < chainImage.GetWidth(); x += 4)
                {
                    if (chainImage.GetPixel(x, y).A < 0.1f) continue;
                    var point = rect.Position + rect.Size * new Vector2(
                        (x + 0.5f) / chainImage.GetWidth(), (y + 0.5f) / chainImage.GetHeight());
                    if (textBounds.Any(bounds => bounds.HasPoint(point))) clear = false;
                }
                Check(clear, "floral chain opaque pixels leave painted reward text unobstructed: " +
                    string.Join(";", textBounds));
                _choices = _reward!.ParentRewardSet!.Rewards.Cast<RelicReward>().Select(r => r.Relic!.Id).ToHashSet();
                _before = p.Relics.Select(r => r.Id).ToHashSet();
            }
            else if (action == "claim-check" || action == "chest-claim")
            {
                var added = p.Relics.Where(r => !_before.Contains(r.Id)).ToList();
                Check(added.Count == 1 && _choices.Contains(added[0].Id), "exactly one offered relic obtained");
                Check(p.Relics.Count(r => _choices.Contains(r.Id)) == 1, "unchosen relic is not obtained");
                if (action == "claim-check")
                {
                    Check(!Descendants(NGame.Instance!).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton>()
                        .Any(button => button.Reward?.ParentRewardSet == _reward!.ParentRewardSet), "claim removes both linked reward buttons");
                    var skipped = _choices.Single(id => id != added[0].Id);
                    Check(p.RunState.CurrentMapPointHistoryEntry.GetEntry(p.NetId).RelicChoices
                        .Any(entry => entry.choice == skipped && !entry.wasPicked), "unchosen reward is recorded as skipped");
                }
            }
            else if (action == "chest-one")
            {
                var choices = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics!;
                const int expected = 1;
                Check(choices.Count == expected, "treasure candidate count " + expected);
                Check(choices.Select(r => r.Id).Distinct().Count() == expected && choices.Select(r => r.Rarity).Distinct().Count() == 1, "treasure candidates are distinct and match rarity");
                var ui = Descendants(NRun.Instance!).OfType<NTreasureRoomRelicCollection>().Single();
                var visible = Descendants(ui).OfType<NTreasureRoomRelicHolder>().Where(h => h.IsVisibleInTree()).ToList();
                Check(visible.Count == expected, "native visible holder count " + expected);
                if (expected == 1)
                    Check(!Descendants(ui).Any(n => n.Name == "FlowerSharedArc"), "ordinary chest has no Flower decoration");
                _before = p.Relics.Select(r => r.Id).ToHashSet();
                _choices = choices.Select(r => r.Id).ToHashSet();
            }
            else if (action == "chest-skip")
            {
                Check(p.Relics.Select(r => r.Id).ToHashSet().SetEquals(_before), "skipping treasure grants no relic");
            }
            else if (action == "empty-setup")
            {
                await RelicCmd.Obtain<SilverCrucible>(p);
            }
            else if (action == "empty")
            {
                var choices = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
                Check(choices == null || choices.Count == 0, "Flower respects empty chest");
                var ui = Descendants(NRun.Instance!).OfType<NTreasureRoomRelicCollection>().Single();
                Check(!Descendants(ui).OfType<NTreasureRoomRelicHolder>().Any(h => h.IsVisibleInTree()), "empty chest has no visible relic");
                Check(!Descendants(ui).Any(n => n.Name == "FlowerSharedArc"), "empty chest has no floral decoration");
            }
            else throw new Exception("Unknown fixture");
            Status = "pass:" + action;
        }
        catch (Exception ex) { Status = "fail:" + action + ":" + ex; throw; }
    }
}
public sealed class FlowerFixtureStatusCmd : AbstractConsoleCmd
{
    public override string CmdName => "flower_fixture_status";
    public override string Args => "";
    public override string Description => "Read Flower regression result.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args) =>
        new(!FlowerFixtureCmd.Status.StartsWith("fail:"), FlowerFixtureCmd.Status);
}
