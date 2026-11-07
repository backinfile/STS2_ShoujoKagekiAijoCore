using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace ShoujoKageki.Core.Telemetry;

internal static class RunHistorySanitizer
{
    private static readonly HashSet<string> Counters = new(StringComparer.Ordinal)
    {
        "karen_shine_current", "karen_shine_max", "core_shine_current", "core_shine_max",
        "CardModel_karen_shine_current", "CardModel_karen_shine_max",
        "CardModel_core_shine_current", "CardModel_core_shine_max"
    };

    internal static string Sanitize(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        string? before = node?.ToJsonString();
        RemoveShineValues(node);
        string? after = node?.ToJsonString();
        return before == after ? json : after ?? json;
    }

    internal static void RemoveShineValues(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in Counters) obj.Remove(key);
            foreach (var property in obj) RemoveShineValues(property.Value);
        }
        else if (node is JsonArray array)
            for (int i = array.Count - 1; i >= 0; i--)
                if (array[i] is JsonObject saved && saved["name"] is JsonValue name && name.TryGetValue<string>(out var text) &&
                    Counters.Contains(text)) array.RemoveAt(i);
                else RemoveShineValues(array[i]);
    }
}
