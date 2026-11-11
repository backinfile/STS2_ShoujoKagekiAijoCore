using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShoujoKageki.Core.Exports
{
    /// <summary>
    /// 精简版卡牌 PNG 导出工具。
    /// 基于 SubViewport 离屏渲染，支持导出卡牌本体、升级版，以及 CardHoverTip 引用卡牌的图，并生成 JSON 索引。
    /// </summary>
    public static partial class CardPngExportUtil
    {
        // ==================== 公共 API ====================

        /// <summary>
        /// 导出所有 卡牌：生成 base、upgraded 图，以及 CardHoverTip 引用卡牌的图，并输出 JSON 索引。
        /// </summary>
        /// <param name="outputDirectory">输出目录，默认 user://CoreCardExports/</param>
        /// <param name="scale">渲染缩放，默认 1</param>
        /// <param name="log">可选日志回调</param>
        public static async Task<ExportResult> ExportAllCardsAsync(
            string? outputDirectory = null,
            float scale = 1f,
            Action<string>? log = null)
        {
            if (!CanExport(out var err))
            {
                log?.Invoke($"[ExportAll] 失败: {err}");
                return new ExportResult
                {
                    Success = false,
                    Error = err,
                };
            }

            var outDir = ProjectSettings.GlobalizePath((outputDirectory ?? "user://CoreCardExports/").Trim());
            Directory.CreateDirectory(outDir);

            var cards = ModelDb.AllCards
                                .OrderBy(c => c.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            log?.Invoke($"[ExportAll] 开始导出 {cards.Count} 张 卡牌到: {outDir}");

            var manifest = new ExportManifest();
            var exportedRefCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var savedImages = 0;
            var failedImages = 0;

            // 第 1 轮：导出所有 卡牌的 base 和 upgraded
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                log?.Invoke($"[ExportAll] ({i + 1}/{cards.Count}) {card.Id.ToString()}");

                try
                {
                    var info = CollectCardInfo(card);

                    // Base
                    var basePath = Path.Combine(outDir, info.Images.Base);
                    if (await ExportSingleAsync(card, basePath, scale, includeHoverTips: false))
                    {
                        log?.Invoke($"  保存 {info.Images.Base}");
                        savedImages++;
                    }
                    else
                    {
                        log?.Invoke($"  失败 {info.Images.Base}");
                        failedImages++;
                    }

                    await WaitFrames(1);

                    // Upgraded
                    if (card.IsUpgradable)
                    {
                        var upgraded = card.ToMutable();
                        upgraded.UpgradeInternal();

                        var upPath = Path.Combine(outDir, info.Images.Upgraded!);
                        if (await ExportSingleAsync(upgraded, upPath, scale, includeHoverTips: false))
                        {
                            log?.Invoke($"  保存 {info.Images.Upgraded}");
                            savedImages++;
                        }
                        else
                        {
                            log?.Invoke($"  失败 {info.Images.Upgraded}");
                            failedImages++;
                        }

                        await WaitFrames(1);
                    }

                    manifest.Cards.Add(info);
                }
                catch (Exception ex)
                {
                    failedImages++;
                    log?.Invoke($"[ExportAll] 错误: {card.Id.ToString()} - {ex.Message}");
                }

                await WaitFrames(2);
            }

            // 第 2 轮：导出所有 CardHoverTip 引用的卡牌（去重）
            log?.Invoke("[ExportAll] 开始导出引用卡牌...");
            foreach (var card in cards)
            {
                foreach (var tip in card.HoverTips)
                {
                    if (tip is not CardHoverTip cardTip || cardTip.Card == null) continue;

                    var refId = cardTip.Card.Id.ToString();
                    if (exportedRefCards.Contains(refId)) continue;

                    var refFileName = $"{SanitizeFileName(refId)}_base.png";
                    var refPath = Path.Combine(outDir, refFileName);

                    if (await ExportSingleAsync(cardTip.Card, refPath, scale, includeHoverTips: false))
                    {
                        log?.Invoke($"  保存引用 {refFileName}");
                        savedImages++;
                        exportedRefCards.Add(refId);
                    }
                    else
                    {
                        log?.Invoke($"  失败引用 {refFileName}");
                        failedImages++;
                    }

                    await WaitFrames(1);

                    if (!cardTip.Card.IsUpgradable) continue;

                    var refUpgradedFileName = $"{SanitizeFileName(refId)}_upgraded.png";
                    var refUpgradedPath = Path.Combine(outDir, refUpgradedFileName);
                    var upgraded = ModelDb.GetById<CardModel>(cardTip.Card.Id).ToMutable();
                    upgraded.UpgradeInternal();

                    if (await ExportSingleAsync(upgraded, refUpgradedPath, scale, includeHoverTips: false))
                    {
                        log?.Invoke($"  保存引用 {refUpgradedFileName}");
                        savedImages++;
                    }
                    else
                    {
                        log?.Invoke($"  失败引用 {refUpgradedFileName}");
                        failedImages++;
                    }

                    await WaitFrames(1);
                }
            }

            // 写入卡牌 JSON
            var jsonPath = Path.Combine(outDir, "cards.json");
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(jsonPath, json);
            await WriteHtmlAsync(outDir, manifest);

            // 写入游戏信息 JSON（卡牌 / 遗物 / 怪物 / 药水）
            try
            {
                var gameInfo = BuildGameInfo();
                var infoJsonPath = Path.Combine(outDir, "game_info.json");
                var infoJson = JsonSerializer.Serialize(gameInfo, JsonOptions);
                await File.WriteAllTextAsync(infoJsonPath, infoJson);
                log?.Invoke($"[ExportAll] 游戏信息已导出: {infoJsonPath}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[ExportAll] 游戏信息导出失败: {ex.Message}");
            }

            log?.Invoke($"[ExportAll] 完成。卡牌: {manifest.Cards.Count}, 引用卡牌: {exportedRefCards.Count}, 图片成功: {savedImages}, 失败: {failedImages}, JSON: {jsonPath}");
            return new ExportResult
            {
                Success = failedImages == 0,
                OutputDirectory = outDir,
                CardCount = manifest.Cards.Count,
                ReferenceCardCount = exportedRefCards.Count,
                SavedImages = savedImages,
                FailedImages = failedImages,
                Error = failedImages == 0 ? null : $"有 {failedImages} 张图片导出失败，请查看日志。",
            };
        }

        /// <summary>
        /// 收集卡牌元数据。
        /// </summary>
        public static async Task<JsonExportResult> ExportJsonOnlyAsync(
            string? outputDirectory = null,
            Action<string>? log = null)
        {
            if (!CanExport(out var err))
            {
                log?.Invoke($"[ExportJsonOnly] 失败: {err}");
                return new JsonExportResult
                {
                    Success = false,
                    Error = err,
                };
            }

            var outDir = ProjectSettings.GlobalizePath((outputDirectory ?? "user://CoreCardExports/").Trim());
            Directory.CreateDirectory(outDir);

            var manifest = new ExportManifest
            {
                Cards = ModelDb.AllCards
                                        .OrderBy(c => c.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(CollectCardInfo)
                    .ToList(),
            };

            var jsonPath = Path.Combine(outDir, "cards.json");
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(jsonPath, json);
            log?.Invoke($"[ExportJsonOnly] 卡牌索引已导出: {jsonPath}");

            var gameInfo = BuildGameInfo();
            var infoJsonPath = Path.Combine(outDir, "game_info.json");
            var infoJson = JsonSerializer.Serialize(gameInfo, JsonOptions);
            await File.WriteAllTextAsync(infoJsonPath, infoJson);
            log?.Invoke($"[ExportJsonOnly] 游戏信息已导出: {infoJsonPath}");

            return new JsonExportResult
            {
                Success = true,
                OutputDirectory = outDir,
                CardCount = manifest.Cards.Count,
            };
        }

        /// <summary>
        /// 导出单张卡牌为 PNG。
        /// </summary>
        public static async Task<bool> ExportSingleAsync(CardModel card, string outputPath, float scale = 1f, bool includeHoverTips = true)
        {
            if (!CanExport(out var err))
            {
                GD.PushError($"[CardPngExportUtil] {err}");
                return false;
            }

            var game = NGame.Instance;
            if (game == null) return false;
            var absPath = ProjectSettings.GlobalizePath(outputPath);
            if (!float.IsFinite(scale) || scale < 0.25f || scale > 4f) return false;

            try
            {
                await using var session = new CardExportSession(game);
                using var image = await session.CaptureAsync(card, scale, includeHoverTips);
                if (image == null) return false;
                var saveError = image.SavePng(absPath);
                if (saveError != Error.Ok)
                    GD.PushError($"[CardPngExportUtil] SavePng failed: {saveError}");
                return saveError == Error.Ok;
            }
            catch (Exception error)
            {
                GD.PushError($"[CardPngExportUtil] {error}");
                return false;
            }
        }

        /// <summary>
        /// 检查当前环境是否可以导出。
        /// </summary>
        public static bool CanExport(out string error)
        {
            if (NGame.Instance == null)
            {
                error = "游戏未加载，请先进入主菜单或战斗。";
                return false;
            }
            error = "";
            return true;
        }

        private static async Task WaitFrames(int count)
        {
            var tree = NGame.Instance?.GetTree();
            if (tree == null) return;
            for (var i = 0; i < count; i++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

    }
}
