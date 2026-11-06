using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using BaseLib.Extensions;


namespace ShoujoKageki.Core.Runtime.Modules.Shine;

public static class ShineHistory
{
    private const string ShineExhaustCountKey = "core_shine_exhaust_count";
    private const string ShineExhaustCardsKey = "core_shine_exhaust_cards";
    private const string ShineExhaustSectionName = "CoreShineExhaustHistorySection";

    private static readonly Dictionary<long, Dictionary<ulong, List<SerializableCard>>> Cache = new();

    public static long? CurrentDisplayedRunStartTime { get; set; }

    public static string InjectIntoHistoryJson(string content)
    {
        try
        {
            var state = RunManager.Instance?.DebugOnlyGetState();
            if (state?.Players == null || state.Players.Count == 0)
                return content;

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return content;
            if (!doc.RootElement.TryGetProperty("players", out var playersElement) || playersElement.ValueKind != JsonValueKind.Array)
                return content;

            using var stream = new MemoryStream(content.Length + 256);
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals("players"))
                {
                    WritePlayersWithShineData(writer, playersElement, state.Players);
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception ex)
        {
            CoreLog.Logger.Warn($"[RunHistoryShinePatch] 注入历史耗尽卡牌失败，保留原始 json: {ex.Message}");
            return content;
        }
    }

    public static void UpdateFromHistoryJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("start_time", out var startTimeElement) || startTimeElement.ValueKind != JsonValueKind.Number)
                return;
            if (!doc.RootElement.TryGetProperty("players", out var playersElement) || playersElement.ValueKind != JsonValueKind.Array)
                return;

            var perPlayer = new Dictionary<ulong, List<SerializableCard>>();
            foreach (var playerElement in playersElement.EnumerateArray())
            {
                if (!playerElement.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
                    continue;

                ulong playerId = idElement.GetUInt64();
                List<SerializableCard> shineExhaustCards = new();

                if ((playerElement.TryGetProperty(ShineExhaustCardsKey, out var cardsElement) || playerElement.TryGetProperty("karen_shine_exhaust_cards", out cardsElement)) && cardsElement.ValueKind == JsonValueKind.Array)
                {
                    shineExhaustCards = JsonSerializer.Deserialize<List<SerializableCard>>(cardsElement.GetRawText()) ?? new List<SerializableCard>();
                }

                perPlayer[playerId] = shineExhaustCards;
            }

            Cache[startTimeElement.GetInt64()] = perPlayer;
        }
        catch (Exception ex)
        {
            CoreLog.Logger.Warn($"[RunHistoryShinePatch] 读取历史耗尽卡牌失败: {ex.Message}");
        }
    }

    public static IReadOnlyList<SerializableCard> GetDisplayedPlayerShineExhaustCards(ulong playerId)
    {
        if (!CurrentDisplayedRunStartTime.HasValue)
            return Array.Empty<SerializableCard>();
        if (!Cache.TryGetValue(CurrentDisplayedRunStartTime.Value, out var perPlayer))
            return Array.Empty<SerializableCard>();
        if (!perPlayer.TryGetValue(playerId, out var cards))
            return Array.Empty<SerializableCard>();

        return cards;
    }

    public static void PopulateExhaustSection(NDeckHistory deckHistory, Player player)
    {
        RemoveExistingExhaustSection(deckHistory);

        var cards = GetDisplayedPlayerShineExhaustCards(player.NetId);
        if (cards.Count == 0)
            return;

        List<CardModel> allCards = new();
        List<CardModel> groupedCards = new();
        List<NDeckHistoryEntry> entries = new();
        foreach (var group in cards.GroupBy(card => card))
        {
            CardModel cardModel = CardModel.FromSerializable(group.Key);
            cardModel.Owner = player;
            groupedCards.Add(cardModel);
            for (int i = 0; i < group.Count(); i++)
            {
                allCards.Add(cardModel);
            }

            NDeckHistoryEntry entry = NDeckHistoryEntry.Create(cardModel, group.Count(), group
                .Where(card => card.FloorAddedToDeck.HasValue)
                .Select(card => card.FloorAddedToDeck!.Value));
            entry.Connect(NDeckHistoryEntry.SignalName.Clicked, Callable.From<NDeckHistoryEntry>(clickedEntry =>
            {
                NGame.Instance?.GetInspectCardScreen().Open(groupedCards, groupedCards.IndexOf(clickedEntry.Card));
            }));
            entries.Add(entry);
        }

        if (entries.Count == 0)
            return;

        var sectionMargin = new MarginContainer
        {
            Name = ShineExhaustSectionName,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        sectionMargin.AddThemeConstantOverride("margin_top", 18);

        var section = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, 80f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        sectionMargin.AddChildSafely(section);

        var headerText = BuildExhaustHeader(cards.Count, allCards);

        var header = new MegaRichTextLabel
        {
            BbcodeEnabled = true,
            Text = headerText,
            CustomMinimumSize = new Vector2(0f, 36f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        CopyHeaderStyle(deckHistory, header);
        section.AddChildSafely(header);

        var marginContainer = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        marginContainer.AddThemeConstantOverride("margin_left", 64);
        marginContainer.AddThemeConstantOverride("margin_top", 4);
        marginContainer.AddThemeConstantOverride("margin_right", 24);
        section.AddChildSafely(marginContainer);

        var cardContainer = new HFlowContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        marginContainer.AddChildSafely(cardContainer);

        foreach (var entry in entries)
        {
            cardContainer.AddChildSafely(entry);
        }

        deckHistory.AddChildSafely(sectionMargin);
    }

    private static string BuildExhaustHeader(int totalCount, IReadOnlyList<CardModel> cards)
    {
        var header = new LocString("gameplay_ui", "CORE_RUN_HISTORY_SHINE_EXHAUST_HEADER");
        header.Add("Count", totalCount);

        string categories = BuildRarityCategories(cards);
        return header.GetFormattedText() + categories;
    }

    private static string BuildRarityCategories(IReadOnlyList<CardModel> cards)
    {
        Dictionary<CardRarity, int> counts = new();
        foreach (CardRarity rarity in Enum.GetValues<CardRarity>())
        {
            counts[rarity] = 0;
        }

        foreach (var card in cards)
        {
            counts[card.Rarity]++;
        }

        var categories = new LocString("run_history", "DECK_HISTORY.categories");
        categories.Add("QuestCards", counts[CardRarity.Quest]);
        categories.Add("EventCards", counts[CardRarity.Event]);
        categories.Add("RareCards", counts[CardRarity.Rare]);
        categories.Add("UncommonCards", counts[CardRarity.Uncommon]);
        categories.Add("CommonCards", counts[CardRarity.Common]);
        categories.Add("CurseCards", counts[CardRarity.Curse]);
        categories.Add("BasicCards", counts[CardRarity.Basic]);

        return categories.GetFormattedText().Trim(',');
    }

    private static void CopyHeaderStyle(NDeckHistory deckHistory, RichTextLabel label)
    {
        var source = deckHistory.GetNodeOrNull<RichTextLabel>("Header");
        if (source == null)
            return;

        label.AddThemeColorOverride("default_color", source.GetThemeColor("default_color"));
        label.AddThemeColorOverride("font_shadow_color", source.GetThemeColor("font_shadow_color"));
        label.AddThemeConstantOverride("shadow_offset_x", source.GetThemeConstant("shadow_offset_x"));
        label.AddThemeConstantOverride("shadow_offset_y", source.GetThemeConstant("shadow_offset_y"));
        label.AddThemeFontOverride("normal_font", source.GetThemeFont("normal_font"));
        label.AddThemeFontOverride("bold_font", source.GetThemeFont("bold_font"));
        label.AddThemeFontSizeOverride("normal_font_size", source.GetThemeFontSize("normal_font_size"));
        label.AddThemeFontSizeOverride("bold_font_size", source.GetThemeFontSize("bold_font_size"));
        label.AddThemeFontSizeOverride("italics_font_size", source.GetThemeFontSize("italics_font_size"));
        label.AddThemeFontSizeOverride("bold_italics_font_size", source.GetThemeFontSize("bold_italics_font_size"));
        label.AddThemeFontSizeOverride("mono_font_size", source.GetThemeFontSize("mono_font_size"));
    }

    private static void RemoveExistingExhaustSection(Node deckHistory)
    {
        var existing = deckHistory.FindChild(ShineExhaustSectionName, recursive: true, owned: false);
        if (existing == null)
            return;

        existing.GetParent()?.RemoveChild(existing);
        existing.QueueFreeSafely();
    }

    private static void WritePlayersWithShineData(Utf8JsonWriter writer, JsonElement playersElement, IReadOnlyList<Player> players)
    {
        writer.WritePropertyName("players");
        writer.WriteStartArray();

        foreach (var playerElement in playersElement.EnumerateArray())
        {
            writer.WriteStartObject();

            ulong? playerId = null;
            foreach (var property in playerElement.EnumerateObject())
            {
                if (property.NameEquals("id") && property.Value.ValueKind == JsonValueKind.Number)
                    playerId = property.Value.GetUInt64();

                if (property.NameEquals(ShineExhaustCountKey) || property.NameEquals(ShineExhaustCardsKey))
                    continue;

                property.WriteTo(writer);
            }

            var player = playerId.HasValue ? players.FirstOrDefault(p => p.NetId == playerId.Value) : null;
            List<SerializableCard> shineExhaustCards = player == null
                ? new List<SerializableCard>()
                : ShineArchive.Snapshots(player).ToList();

            writer.WriteNumber(ShineExhaustCountKey, shineExhaustCards.Count);
            writer.WritePropertyName(ShineExhaustCardsKey);
            JsonSerializer.Serialize(writer, shineExhaustCards);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}

[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile), new[] { typeof(string), typeof(string) })]
internal static class RunHistoryWriteFilePatch
{
    [HarmonyPrefix]
    private static void Prefix(string path, ref string content)
    {
        if (!IsRunHistoryPath(path) || string.IsNullOrWhiteSpace(content))
            return;

        content = ShineHistory.InjectIntoHistoryJson(content);
    }

    private static bool IsRunHistoryPath(string path)
    {
        return path.EndsWith(".run", StringComparison.OrdinalIgnoreCase)
            && path.Replace('\\', '/').Contains("/history/", StringComparison.OrdinalIgnoreCase);
    }
}

[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.ReadFile))]
internal static class RunHistoryReadFilePatch
{
    [HarmonyPostfix]
    private static void Postfix(string path, string? __result)
    {
        if (!IsRunHistoryPath(path))
            return;

        ShineHistory.UpdateFromHistoryJson(__result);
    }

    private static bool IsRunHistoryPath(string path)
    {
        return path.EndsWith(".run", StringComparison.OrdinalIgnoreCase)
            && path.Replace('\\', '/').Contains("/history/", StringComparison.OrdinalIgnoreCase);
    }
}

[HarmonyPatch(typeof(NRunHistory), "DisplayRun")]
internal static class NRunHistoryDisplayRunPatch
{
    [HarmonyPrefix]
    private static void Prefix(RunHistory history)
    {
        ShineHistory.CurrentDisplayedRunStartTime = history.StartTime;
    }
}

[HarmonyPatch(typeof(NDeckHistory), nameof(NDeckHistory.LoadDeck))]
internal static class NDeckHistoryLoadDeckPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDeckHistory __instance, Player player)
    {
        ShineHistory.PopulateExhaustSection(__instance, player);
    }
}
