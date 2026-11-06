using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

internal sealed class PlayerRuleFieldsHandler : PlayerExtraFieldsHandlerBase<CoreRunRules>
{
    public override string FieldName => "core_rules";
    public override void ReadPlayerField(JsonElement extraFieldsElement, MegaCrit.Sts2.Core.Saves.SerializableExtraPlayerFields extraFields)
    {
        var hasCoreRules = extraFieldsElement.ValueKind == JsonValueKind.Object && extraFieldsElement.TryGetProperty(FieldName, out _);
        var hasLegacyArchive = extraFieldsElement.ValueKind == JsonValueKind.Object && extraFieldsElement.TryGetProperty("karen_player_disposed_pile", out _);
        if (!hasCoreRules && !hasLegacyArchive)
            extraFieldsElement = JsonSerializer.SerializeToElement(new Dictionary<string, CoreRunRules>
            {
                [FieldName] = new(V1.Shine.RulesetId, CombatCardGainRelicMode.ShoujoOnly, RuntimeEntry.Fingerprint)
            });
        base.ReadPlayerField(extraFieldsElement, extraFields);
    }
    protected override CoreRunRules Collect(Player player) => ShineArchive.GetRules(player) with { NetworkFingerprint = RuntimeEntry.Fingerprint };
    protected override void Restore(Player player, CoreRunRules data)
    {
        if (data.RulesetId != V1.Shine.RulesetId && data.RulesetId != ShineArchive.LegacyRulesetId) throw new InvalidDataException($"Unsupported saved Core rules {data.RulesetId}.");
        if (data.NetworkFingerprint.Length > 0 && data.NetworkFingerprint != RuntimeEntry.Fingerprint)
            CoreLog.Logger.Info("Core save restored with its original rule snapshot.");
        ShineArchive.SetRules(player, data);
    }
    protected override void WriteNetData(PacketWriter writer, CoreRunRules data) => writer.WriteString(JsonSerializer.Serialize(data));
    protected override CoreRunRules ReadNetData(PacketReader reader)
    {
        var rules = JsonSerializer.Deserialize<CoreRunRules>(reader.ReadString())!;
        if (rules.NetworkFingerprint != RuntimeEntry.Fingerprint) throw new InvalidDataException("Core network fingerprint differs from the host.");
        return rules;
    }
    protected override CoreRunRules CreateEmptyData() => new(ShineArchive.LegacyRulesetId, CombatCardGainRelicMode.Enabled, "");
}
