using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ShoujoKageki.Core.Runtime.Modules.Shine.Saves;

/// <summary>
/// 在 GodotFileIo 最底层拦截文件读写，将 Mod 数据嵌入游戏存档 JSON 的 players[*].extra_fields。
///
/// 写入流程：
///   RunSaveManager.SaveRun → GodotFileIo.WriteFile(path, bytes)
///   ↳ [Prefix] 将闪耀牌堆注入 players[*].extra_fields → 写入文件
///
/// 读取流程：
///   MigrationManager.LoadSaveFromPath → GodotFileIo.ReadFile(path) → string
///   ↳ [Postfix] 在一次 LoadRunSave / LoadMultiplayerRunSave 调用内捕获 JSON
///
/// 扩展字段绑定到本次返回的 SerializableRun，随 Player.FromSerializable 恢复。
/// 菜单预读其他文件不会覆盖已加载对象的数据，也没有全局待消费缓冲区。
/// 时机与游戏完全一致，数据嵌入同一个 .save 文件，无需伴随文件。
/// System.Text.Json 默认忽略未知字段，游戏的反序列化不受影响。
/// </summary>
[HarmonyPatch]
internal static class RunSaveManager_Patches
{
    private const string PlayersKey = "players";
    private const string ExtraFieldsKey = "extra_fields";
    private const string NetIdKey = "net_id";

    /// <summary>
    /// 判断路径是否为任意局内存档（单机或联机，含 .backup 后缀）
    /// </summary>
    private static bool IsRunSavePath(string path)
        => path.Contains(RunSaveManager.runSaveFileName)
        || path.Contains(RunSaveManager.multiplayerRunSaveFileName);

    // ─────────────────────────────────────────────────────────────────────
    // 写入：同步版本
    // WriteFile(string, string) 内部会调用 WriteFile(string, byte[])，只需 patch byte[] 版本
    // ─────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile),
        new[] { typeof(string), typeof(byte[]) })]
    [HarmonyPrefix]
    private static void WriteFile_Prefix(string path, ref byte[] bytes)
    {
        if (!IsRunSavePath(path)) return;
        bytes = InjectModData(bytes);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 写入：异步版本（SaveRun 默认走此分支）
    // async Task 方法打 Prefix 在方法体执行前运行，ref 修改对 bytes 有效
    // ─────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFileAsync),
        new[] { typeof(string), typeof(byte[]) })]
    [HarmonyPrefix]
    private static void WriteFileAsync_Prefix(string path, ref byte[] bytes)
    {
        if (!IsRunSavePath(path)) return;
        bytes = InjectModData(bytes);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 读取：MigrationManager.LoadSaveFromPath 调用 ReadFile(path) 获取 JSON 字符串
    // 仅原生读档作用域内记录结果；普通文件预读不会污染其他存档。
    // ─────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.ReadFile))]
    [HarmonyPostfix]
    private static void ReadFile_Postfix(string path, ref string? __result)
    {
        if (!IsRunSavePath(path)) return;
        KarenRunSaveReadScope.Record(__result);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 实现：注入
    // ─────────────────────────────────────────────────────────────────────
    private static byte[] InjectModData(byte[] originalBytes)
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null) return originalBytes;

            using var doc = JsonDocument.Parse(originalBytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return originalBytes;
            if (!doc.RootElement.TryGetProperty(PlayersKey, out var playersElement) || playersElement.ValueKind != JsonValueKind.Array)
                return originalBytes;

            using var ms = new MemoryStream(originalBytes.Length + 256);
            using var writer = new Utf8JsonWriter(ms);

            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals(PlayersKey))
                {
                    WritePlayersWithInjectedFields(writer, playersElement, state.Players);
                    continue;
                }

                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.Flush();

            CoreLog.Logger.Info("[SaveSystem] 已将 extra_fields 扩展字段注入存档");
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            CoreLog.Logger.Error($"[SaveSystem] 注入 Mod 数据失败，写入原始数据: {ex.Message}");
            return originalBytes; // 保证不损坏存档
        }
    }

    [HarmonyPatch]
    private static class NativeRunSaveReadPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(RunSaveManager), nameof(RunSaveManager.LoadRunSave));
            yield return AccessTools.Method(typeof(RunSaveManager), nameof(RunSaveManager.LoadMultiplayerRunSave));
        }

        private static void Prefix(out KarenRunSaveReadScope __state)
            => __state = new KarenRunSaveReadScope();

        private static void Postfix(ReadSaveResult<SerializableRun> __result, KarenRunSaveReadScope __state)
        {
            try { __state.Bind(__result); }
            catch (Exception ex)
            {
                CoreLog.Logger.Error($"[SaveSystem] 绑定存档扩展字段失败: {ex}");
            }
        }

        private static Exception? Finalizer(Exception? __exception, KarenRunSaveReadScope? __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    private static void WritePlayersWithInjectedFields(
        Utf8JsonWriter writer,
        JsonElement playersElement,
        IReadOnlyList<Player> players)
    {
        writer.WritePropertyName(PlayersKey);
        writer.WriteStartArray();

        foreach (var playerElement in playersElement.EnumerateArray())
        {
            Player? player = null;
            if (TryReadPlayerId(playerElement, out var playerId))
                player = FindPlayer(players, playerId);

            writer.WriteStartObject();
            foreach (var prop in playerElement.EnumerateObject())
            {
                if (prop.NameEquals(ExtraFieldsKey) && player != null)
                {
                    WriteExtraFields(writer, prop.Value, player);
                    continue;
                }

                prop.WriteTo(writer);
            }

            if (!playerElement.TryGetProperty(ExtraFieldsKey, out _) && player != null)
                WriteExtraFields(writer, default, player);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteExtraFields(
        Utf8JsonWriter writer,
        JsonElement extraFieldsElement,
        Player player)
    {
        writer.WritePropertyName(ExtraFieldsKey);
        writer.WriteStartObject();

        if (extraFieldsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in extraFieldsElement.EnumerateObject())
            {
                if (IsManagedExtraField(prop))
                    continue;

                prop.WriteTo(writer);
            }
        }

        foreach (var handler in PlayerExtraFieldsHandlers.All)
            handler.WritePlayerField(writer, player);

        writer.WriteEndObject();
    }

    private static bool TryReadPlayerId(JsonElement playerElement, out ulong playerId)
    {
        playerId = default;
        return playerElement.TryGetProperty(NetIdKey, out var idElement)
            && idElement.ValueKind == JsonValueKind.Number
            && idElement.TryGetUInt64(out playerId);
    }

    private static Player? FindPlayer(IReadOnlyList<Player> players, ulong playerId)
    {
        foreach (var player in players)
        {
            if (player.NetId == playerId)
                return player;
        }

        return null;
    }

    private static bool IsManagedExtraField(JsonProperty prop)
    {
        if (prop.NameEquals("karen_player_disposed_pile")) return true;
        foreach (var handler in PlayerExtraFieldsHandlers.All)
        {
            if (prop.NameEquals(handler.FieldName))
                return true;
        }

        return false;
    }
}
