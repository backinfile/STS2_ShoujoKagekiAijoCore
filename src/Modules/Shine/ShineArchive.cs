using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime.Modules.Shine;

public sealed record CoreRunRules(string RulesetId, CombatCardGainRelicMode RelicMode, string NetworkFingerprint);
public static class ShineArchive
{
    public const string LegacyRulesetId = "karen-shine@legacy-1";
    private sealed class Archive
    {
        public readonly List<SerializableCard> Cards = [];
        public readonly List<string> Lifecycles = [];
        public CoreRunRules Rules = new(V1.Shine.RulesetId, CoreApi.Configuration.Current.CombatCardGainRelicMode, RuntimeEntry.Fingerprint);
    }
    public static event Action<Player>? Changed;
    private static readonly ConditionalWeakTable<Player, Archive> Data = new();
    private static Archive For(Player player) => Data.GetOrCreateValue(player);
    public static CoreRunRules GetRules(Player player) => For(player).Rules;
    public static void SetRules(Player player, CoreRunRules rules) => For(player).Rules = rules;
    public static IReadOnlyList<SerializableCard> Snapshots(Player player) => Array.AsReadOnly(For(player).Cards.ToArray());
    public static IReadOnlyList<CardModel> Models(Player player) => Snapshots(player).Select(save =>
    {
        using var scope = ShineService.AllowSnapshotCopy();
        var card = CardModel.FromSerializable(save);
        card.Owner = player;
        return card;
    }).ToArray();
    public static void Restore(Player player, IEnumerable<SerializableCard> snapshots)
    {
        var archive = For(player);
        archive.Cards.Clear();
        archive.Cards.AddRange(snapshots);
        archive.Lifecycles.Clear();
        archive.Lifecycles.AddRange(archive.Cards.Select((_, i) => $"{player.NetId}:{i + 1}"));
        Changed?.Invoke(player);
    }
    internal static string Add(Player player, CardModel card)
    {
        var archive = For(player);
        archive.Cards.Add(card.ToSerializable());
        var lifecycle = $"{player.NetId}:{archive.Cards.Count}";
        archive.Lifecycles.Add(lifecycle);
        Changed?.Invoke(player);
        return lifecycle;
    }
    internal static IReadOnlyList<DepletedCard> Entries(Player player)
    {
        var archive = For(player);
        return Models(player).Select((card, i) => new DepletedCard(card.Id.ToString(), card.CurrentUpgradeLevel,
            CoreApi.Get<IShineService>().GetState(card)?.Maximum ?? 0, archive.Lifecycles[i])).ToArray();
    }
    internal static CardModel Reacquire(Player player, DepletedCard entry)
    {
        var archive = For(player);
        var index = archive.Lifecycles.IndexOf(entry.LifecycleId);
        if (index < 0) throw new ArgumentException("The depleted card is not in this player's archive.", nameof(entry));
        using var scope = ShineService.AllowSnapshotCopy();
        var card = CardModel.FromSerializable(archive.Cards[index]);
        card.Owner = player;
        ShineService.Instance.ResetLifecycle(card);
        return card;
    }
}
