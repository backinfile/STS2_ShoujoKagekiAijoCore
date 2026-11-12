using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Saves;
using BaseLib.Patches.Saves;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;
namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

public sealed class CorePlayerSave : ICoreModule, IPlayerStateEvents
{
    private sealed class CardConverter : JsonConverter<SerializableCard>
    {
        public override SerializableCard Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var card = document.RootElement;
            // Historical extra_fields used reflection serialization, including object-form ModelId.
            return card.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<SerializableCard>(card.GetRawText())!
                : JsonSerializer.Deserialize(card.GetRawText(), JsonSerializationUtility.GetTypeInfo<SerializableCard>())!;
        }
        public override void Write(Utf8JsonWriter writer, SerializableCard card, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, card, JsonSerializationUtility.GetTypeInfo<SerializableCard>());
    }
    private static readonly JsonSerializerOptions Options = new() { Converters = { new CardConverter() } };
    private const string SaveId = "ShoujoKagekiAijoCore.player-state@1";
    private sealed record Snapshot(int Format, List<SerializableCard> Cards, CoreRunRules Rules);
    private static readonly ConditionalWeakTable<SerializablePlayer, Snapshot> Legacy = new();
    private static readonly ConditionalWeakTable<Player, object> Applied = new();
    private static CorePlayerSave _instance = null!;
    public event Action<Player>? Restored;
    public string Id => "player-save";
    public int Order => -450;
    public void Initialize(CoreContext context)
    {
        _instance = this;
        context.Services.Add<IPlayerStateEvents>(this);
        ExtendedSaveTypes.RegisterSavedValue<Player, string>(SaveId, Capture, Restore,
            (json, writer) => writer.WriteString(json), ReadNetwork);
    }
    private static string Capture(Player player) => JsonSerializer.Serialize(new Snapshot(1,
        ShineArchive.Snapshots(player).ToList(), ShineArchive.GetRules(player) with { NetworkFingerprint = RuntimeEntry.Fingerprint }), Options);
    private static Snapshot Decode(string json)
    {
        var state = JsonSerializer.Deserialize<Snapshot>(json, Options) ?? throw new InvalidDataException("Missing Core player state.");
        if (state.Format != 1 || state.Cards == null || state.Rules == null) throw new InvalidDataException("Unsupported Core player state.");
        ValidateRules(state.Rules);
        return state;
    }
    private static void ValidateRules(CoreRunRules rules)
    {
        if (rules.RulesetId != V1.Shine.RulesetId && rules.RulesetId != ShineArchive.LegacyRulesetId)
            throw new InvalidDataException($"Unsupported saved Core rules {rules.RulesetId}.");
    }
    private static string ReadNetwork(PacketReader reader)
    {
        var json = reader.ReadString();
        if (Decode(json).Rules.NetworkFingerprint != RuntimeEntry.Fingerprint)
            throw new InvalidDataException("Core network fingerprint differs from the host.");
        return json;
    }
    private static void Restore(Player player, string? json)
    {
        if (json == null) return;
        Apply(player, Decode(json)); Applied.GetOrCreateValue(player);
    }
    private static void Apply(Player player, Snapshot state)
    {
        ValidateRules(state.Rules);
        ShineArchive.SetRules(player, state.Rules);
        ShineArchive.Restore(player, state.Cards);
    }
    internal static void BindLegacy(SerializablePlayer player, JsonElement extra)
    {
        if (ExtendedSaveHandlers<Player, SerializablePlayer>.ExtendedData[player].DictForType<string>().ContainsKey(SaveId)) return;
        bool hasLegacy = extra.ValueKind == JsonValueKind.Object && extra.TryGetProperty("karen_player_disposed_pile", out _);
        var cards = new List<SerializableCard>();
        CoreRunRules rules = new(hasLegacy ? ShineArchive.LegacyRulesetId : V1.Shine.RulesetId,
            hasLegacy ? CombatCardGainRelicMode.Enabled : CombatCardGainRelicMode.ShoujoOnly, "");
        if (extra.ValueKind == JsonValueKind.Object)
        {
            if ((extra.TryGetProperty("core_player_disposed_pile", out var archive) || extra.TryGetProperty("karen_player_disposed_pile", out archive)) && archive.ValueKind != JsonValueKind.Null)
                cards = JsonSerializer.Deserialize<List<SerializableCard>>(archive.GetRawText(), Options) ?? [];
            if (extra.TryGetProperty("core_rules", out var savedRules) && savedRules.ValueKind != JsonValueKind.Null)
                rules = JsonSerializer.Deserialize<CoreRunRules>(savedRules.GetRawText()) ?? rules;
        }
        ValidateRules(rules);
        var migrated = new Snapshot(1, cards, rules with { NetworkFingerprint = RuntimeEntry.Fingerprint });
        Legacy.AddOrUpdate(player, migrated);
        // Canonicalized saves can be sent directly, before constructing a Player.
        ExtendedSaveHandlers<Player, SerializablePlayer>.ExtendedData[player].DictForType<string>()[SaveId] = JsonSerializer.Serialize(migrated, Options);
    }
    [HarmonyPatch(typeof(Player), nameof(Player.FromSerializable))]
    private static class LegacyRestore
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(SerializablePlayer save, Player __result)
        {
            if (!Applied.TryGetValue(__result, out _) && Legacy.TryGetValue(save, out var old)) Apply(__result, old);
            _instance.Restored?.Invoke(__result);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.SyncWithSerializedPlayer))]
    private static class Sync
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance, SerializablePlayer player)
        {
            Applied.Remove(__instance);
            ExtendedSaveHandlers<Player, SerializablePlayer>.Load(player, __instance);
            if (!Applied.TryGetValue(__instance, out _) && Legacy.TryGetValue(player, out var old)) Apply(__instance, old);
            _instance.Restored?.Invoke(__instance);
        }
    }
}
