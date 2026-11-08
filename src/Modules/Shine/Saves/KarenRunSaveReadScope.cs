using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using System;
using System.Text.Json;
using System.Threading;

namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

/// <summary>
/// 仅捕获一次原生读档调用中的 JSON，并将扩展数据绑定到它返回的存档对象。
/// 菜单可以预读多个存档，也可以重复使用缓存的 SerializableRun，互不消费数据。
/// </summary>
internal sealed class KarenRunSaveReadScope : IDisposable
{
    private static readonly AsyncLocal<KarenRunSaveReadScope?> Current = new();
    private readonly KarenRunSaveReadScope? _parent;
    private string? _json;

    public KarenRunSaveReadScope()
    {
        _parent = Current.Value;
        Current.Value = this;
    }

    public static void Record(string? json)
    {
        if (Current.Value is { } scope)
            scope._json = json;
    }

    public void Bind(ReadSaveResult<SerializableRun> result)
    {
        if (!result.Success || result.SaveData == null || string.IsNullOrWhiteSpace(_json))
            return;

        using var doc = JsonDocument.Parse(_json);
        if (!doc.RootElement.TryGetProperty("players", out var players)
            || players.ValueKind != JsonValueKind.Array)
            return;

        foreach (var player in result.SaveData.Players)
        {
            JsonElement extra = default;
            foreach (var raw in players.EnumerateArray())
            {
                if (raw.TryGetProperty("net_id", out var id) && id.TryGetUInt64(out var netId)
                    && netId == player.NetId)
                {
                    raw.TryGetProperty("extra_fields", out extra);
                    break;
                }
            }

            CorePlayerSave.BindLegacy(player, extra);
        }
    }

    public void Dispose() => Current.Value = _parent;
}
