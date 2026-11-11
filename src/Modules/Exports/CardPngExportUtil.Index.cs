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
    public static partial class CardPngExportUtil
    {
        // ---- JSON 序列化选项 ----
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // ==================== 数据模型 ====================

        public sealed class CardExportInfo
        {
            public string SourceMod { get; set; } = "";
            public string SourceVersion { get; set; } = "";
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("playerId")]
            public string? PlayerId { get; set; }

            [JsonPropertyName("cost")]
            public string Cost { get; set; } = "";

            [JsonPropertyName("rarity")]
            public string Rarity { get; set; } = "";

            [JsonPropertyName("type")]
            public string Type { get; set; } = "";

            [JsonPropertyName("tips")]
            public List<TipInfo> Tips { get; set; } = new();

            [JsonPropertyName("images")]
            public ImageInfo Images { get; set; } = new();
        }

        public sealed class TipInfo
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "";

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("cardId")]
            public string? CardId { get; set; }

            [JsonPropertyName("image")]
            public string? Image { get; set; }

            [JsonPropertyName("upgradedImage")]
            public string? UpgradedImage { get; set; }

            [JsonPropertyName("isUpgraded")]
            public bool? IsUpgraded { get; set; }
        }

        public sealed class ImageInfo
        {
            [JsonPropertyName("base")]
            public string Base { get; set; } = "";

            [JsonPropertyName("upgraded")]
            public string? Upgraded { get; set; }
        }

        public sealed class ExportManifest
        {
            [JsonPropertyName("cards")]
            public List<CardExportInfo> Cards { get; set; } = new();
        }

        public sealed class GameInfoExport
        {
            [JsonPropertyName("cards")]
            public List<CardGameInfo> Cards { get; set; } = new();

            [JsonPropertyName("relics")]
            public List<IdNamePair> Relics { get; set; } = new();

            [JsonPropertyName("potions")]
            public List<IdNamePair> Potions { get; set; } = new();

            [JsonPropertyName("monsters")]
            public List<IdNamePair> Monsters { get; set; } = new();

            [JsonPropertyName("encounters")]
            public List<IdNamePair> Encounters { get; set; } = new();

            [JsonPropertyName("events")]
            public List<IdNamePair> Events { get; set; } = new();

            [JsonPropertyName("characters")]
            public List<IdNamePair> Characters { get; set; } = new();
        }

        public sealed class IdNamePair
        {
            public string SourceMod { get; set; } = "";
            public string SourceVersion { get; set; } = "";
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";
        }

        public sealed class CardGameInfo
        {
            public string SourceMod { get; set; } = "";
            public string SourceVersion { get; set; } = "";
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("playerId")]
            public string? PlayerId { get; set; }

            [JsonPropertyName("cost")]
            public string Cost { get; set; } = "";

            [JsonPropertyName("rarity")]
            public string Rarity { get; set; } = "";

            [JsonPropertyName("type")]
            public string Type { get; set; } = "";
        }

        public sealed class ExportResult
        {
            public bool Success { get; init; }
            public string OutputDirectory { get; init; } = "";
            public int CardCount { get; init; }
            public int ReferenceCardCount { get; init; }
            public int SavedImages { get; init; }
            public int FailedImages { get; init; }
            public string? Error { get; init; }
        }

        public sealed class JsonExportResult
        {
            public bool Success { get; init; }
            public string OutputDirectory { get; init; } = "";
            public int CardCount { get; init; }
            public string? Error { get; init; }
        }

        private static ShoujoKageki.Core.V1.CoreModelInfo Source(AbstractModel model)
        {
            var source = ExportsModule.Context.Discovery.Models.FirstOrDefault(item => item.ModelType == model.GetType())
                ?? new(model.Id.ToString(), model.GetType().Name, model.GetType().Assembly.GetName().Name ?? "unknown", model.GetType().Assembly.GetName().Version?.ToString() ?? "unknown", model.GetType());
            return string.IsNullOrWhiteSpace(source.ModVersion)
                ? source with { ModVersion = source.ModId == "sts2" ? ExportsModule.Context.GameVersion : "unknown" }
                : source;
        }

        private static Task WriteHtmlAsync(string directory, ExportManifest manifest)
        {
            static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");
            var html = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>卡牌资料</title><style>body{font:16px system-ui;margin:24px;background:#faf8f3;color:#242820}main{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:24px}img{width:100%;height:auto}figure{margin:0}small{display:block;overflow-wrap:anywhere}</style><main>");
            foreach (var card in manifest.Cards)
            {
                html.Append("<figure><img loading=\"lazy\" src=\"").Append(Encode(card.Images.Base)).Append("\" alt=\"").Append(Encode(card.Name)).Append("\"><figcaption>").Append(Encode(card.Name)).Append("<small>").Append(Encode(card.Id)).Append(" · ").Append(Encode(card.SourceMod)).Append(" ").Append(Encode(card.SourceVersion)).Append("</small></figcaption>");
                if (card.Images.Upgraded != null) html.Append("<details><summary>升级</summary><img loading=\"lazy\" src=\"").Append(Encode(card.Images.Upgraded)).Append("\" alt=\"升级\"></details>");
                html.Append("</figure>");
            }
            html.Append("</main></html>");
            return File.WriteAllTextAsync(Path.Combine(directory, "index.html"), html.ToString());
        }

        private static CardExportInfo CollectCardInfo(CardModel card)
        {
            var baseName = SanitizeFileName(card.Id.ToString());
            var info = new CardExportInfo
            {
                Id = card.Id.ToString(),
                SourceMod = Source(card).ModId,
                SourceVersion = Source(card).ModVersion,
                Name = card.Title?.ToString() ?? card.Id.ToString(),
                PlayerId = GetPlayerIdByCardPool(card),
                Cost = GetCostString(card),
                Rarity = GetRarityString(card.Rarity),
                Type = GetTypeString(card.Type),
                Images = new ImageInfo
                {
                    Base = $"{baseName}_base.png",
                    Upgraded = card.IsUpgradable ? $"{baseName}_upgraded.png" : null,
                },
            };

            foreach (var tip in card.HoverTips)
            {
                switch (tip)
                {
                    case HoverTip hoverTip:
                        info.Tips.Add(new TipInfo
                        {
                            Type = "text",
                            Title = hoverTip.Title?.ToString(),
                            Description = hoverTip.Description,
                        });
                        break;
                    case CardHoverTip cardTip:
                        var refId = cardTip.Card?.Id.ToString();
                        var refBaseName = refId != null ? SanitizeFileName(refId) : null;
                        info.Tips.Add(new TipInfo
                        {
                            Type = "card",
                            CardId = refId,
                            Image = refBaseName != null ? $"{refBaseName}_base.png" : null,
                            UpgradedImage = refId != null && cardTip.Card != null && cardTip.Card.IsUpgradable ? $"{SanitizeFileName(refId)}_upgraded.png" : null,
                            IsUpgraded = false,
                        });
                        AddUpgradedTokenTip(info.Tips, cardTip.Card);
                        break;
                }
            }

            return info;
        }

        private static string? GetPlayerIdByCardPool(CardModel card)
        {
            var cardPool = card.Pool;
            return ModelDb.AllCharacters
                .FirstOrDefault(character => character.CardPool == cardPool)
                ?.Id.ToString();
        }

        private static void AddUpgradedTokenTip(List<TipInfo> tips, CardModel? card)
        {
            if (card == null)
                return;

            if (card.Rarity != CardRarity.Token || !card.IsUpgradable)
                return;

            var refId = card.Id.ToString();
            tips.Add(new TipInfo
            {
                Type = "card",
                CardId = refId,
                Image = $"{SanitizeFileName(refId)}_upgraded.png",
                IsUpgraded = true,
            });
        }

        private static GameInfoExport BuildGameInfo()
        {
            return new GameInfoExport
            {
                Cards = ModelDb.AllCards
                    .OrderBy(c => c.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(CollectCardGameInfo)
                    .ToList(),
                Relics = ModelDb.AllRelics
                    .OrderBy(r => r.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(r => new IdNamePair { Id = r.Id.ToString(), Name = r.Title?.GetFormattedText() ?? r.Id.ToString(), SourceMod = Source(r).ModId, SourceVersion = Source(r).ModVersion })
                    .ToList(),
                Potions = ModelDb.AllPotions
                    .OrderBy(p => p.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(p => new IdNamePair { Id = p.Id.ToString(), Name = p.Title?.GetFormattedText() ?? p.Id.ToString(), SourceMod = Source(p).ModId, SourceVersion = Source(p).ModVersion })
                    .ToList(),
                Monsters = ModelDb.Monsters
                    .OrderBy(m => m.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(m => new IdNamePair { Id = m.Id.ToString(), Name = m.Title?.GetFormattedText() ?? m.Id.ToString(), SourceMod = Source(m).ModId, SourceVersion = Source(m).ModVersion })
                    .ToList(),
                Encounters = ModelDb.AllEncounters
                    .OrderBy(e => e.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(e => new IdNamePair { Id = e.Id.ToString(), Name = e.Title?.GetFormattedText() ?? e.Id.ToString(), SourceMod = Source(e).ModId, SourceVersion = Source(e).ModVersion })
                    .ToList(),
                Events = ModelDb.AllEvents
                    .Concat(ModelDb.AllAncients)
                    .GroupBy(e => e.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(e => e.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(e => new IdNamePair { Id = e.Id.ToString(), Name = e.Title?.GetFormattedText() ?? e.Id.ToString(), SourceMod = Source(e).ModId, SourceVersion = Source(e).ModVersion })
                    .ToList(),
                Characters = ModelDb.AllCharacters
                    .OrderBy(c => c.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(c => new IdNamePair { Id = c.Id.ToString(), Name = c.Title?.GetFormattedText() ?? c.Id.ToString(), SourceMod = Source(c).ModId, SourceVersion = Source(c).ModVersion })
                    .ToList(),
            };
        }

        private static CardGameInfo CollectCardGameInfo(CardModel card)
        {
            return new CardGameInfo
            {
                Id = card.Id.ToString(),
                SourceMod = Source(card).ModId,
                SourceVersion = Source(card).ModVersion,
                Name = card.Title ?? card.Id.ToString(),
                PlayerId = GetPlayerIdByCardPool(card),
                Cost = GetCostString(card),
                Rarity = GetRarityString(card.Rarity),
                Type = GetTypeString(card.Type),
            };
        }

        /// <summary>
        /// 获取卡牌费用字符串。
        /// </summary>
        private static string GetCostString(CardModel card)
        {
            var hasX = (bool?)AccessTools.Property(typeof(CardModel), "HasEnergyCostX")?.GetValue(card);
            if (hasX == true)
                return "X";

            var resolved = card.EnergyCost?.GetResolved() ?? 0;
            if (resolved < 0)
                return "";

            return resolved.ToString();
        }

        /// <summary>
        /// 稀有度转中文。
        /// </summary>
        private static string GetRarityString(CardRarity rarity) => rarity switch
        {
            CardRarity.Basic => "基础",
            CardRarity.Common => "普通",
            CardRarity.Uncommon => "罕见",
            CardRarity.Rare => "稀有",
            CardRarity.Ancient => "远古",
            CardRarity.Event => "事件",
            CardRarity.Token => "衍生",
            CardRarity.Status => "状态",
            CardRarity.Curse => "诅咒",
            CardRarity.Quest => "任务",
            _ => rarity.ToString(),
        };

        /// <summary>
        /// 类型转中文。
        /// </summary>
        private static string GetTypeString(CardType type) => type switch
        {
            CardType.Attack => "攻击",
            CardType.Skill => "技能",
            CardType.Power => "能力",
            CardType.Status => "状态",
            CardType.Curse => "诅咒",
            _ => type.ToString(),
        };

        private static string SanitizeFileName(string value)
        {
            string readable = new(value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
            return readable + "_" + digest;
        }

    }
}
