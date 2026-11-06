using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ShoujoKageki.Core.Telemetry;

/// <summary>Bounded, engine-independent aggregation. No disk or network work in log callbacks.</summary>
internal sealed class ClientErrorBuffer
{
    internal sealed record ModInfo(string id, string name, string version);
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal const int MaxGroups = 64;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _build, _version, _platform, _os, _gameCommit;
    private readonly ModInfo[] _mods;
    private readonly bool _modsTruncated;
    private readonly JsonNode? _coreContext;
    private DateTimeOffset _window;
    private int _captures, _snapshots;
    private long _suppressed;

    private sealed class Entry
    {
        internal string Id = Guid.NewGuid().ToString("N");
        internal string Source = "", Message = "", Stack = "";
        internal long First, Last;
        internal int Count, Saved;
    }

    internal ClientErrorBuffer(string build, string version, string platform, Func<DateTimeOffset>? clock = null,
        string os = "unknown", string gameCommit = "unknown", IEnumerable<ModInfo>? mods = null, Func<object>? coreContext = null)
    {
        _build = Clean(build, 80); _version = Clean(version, 80); _platform = Clean(platform, 40);
        _os = Clean(os, 40); _gameCommit = Clean(gameCommit, 80);
        var selected = new List<ModInfo>(); int bytes = 2;
        foreach (var mod in mods ?? Array.Empty<ModInfo>())
        {
            var bounded = new ModInfo(Clean(mod.id, 96), Clean(string.IsNullOrWhiteSpace(mod.name) ? mod.id : mod.name, 80),
                Clean(string.IsNullOrWhiteSpace(mod.version) ? "unknown" : mod.version, 40));
            bytes += Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(bounded, JsonOptions)) + 1;
            if (selected.Count >= 128 || bytes > 16384) { _modsTruncated = true; break; }
            selected.Add(bounded);
        }
        _mods = selected.ToArray();
        if (coreContext != null)
        {
            _coreContext = JsonSerializer.SerializeToNode(coreContext(), JsonOptions)
                ?? throw new InvalidDataException("Core error context is missing.");
            _modsTruncated |= BoundCoreContext(_coreContext);
        }
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _window = _clock();
    }

    // Non-backtracking expressions and bounded input also bound work for hostile error text.
    private static string Replace(string text, string pattern, string replacement) =>
        Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    internal static string Clean(string? text, int limit)
    {
        text ??= "";
        if (text.Length > 16384) text = text[..16384];
        text = Replace(text, @"(?:https?|wss?)://[^\s<>""']+", "<url>");
        // A drive letter must not be the tail of a resource URI scheme (res://, event:/).
        text = Replace(text, @"(?:(^|[^a-z])[a-z]:[\\/]|\\\\)[^\r\n""<>|]+", "$1<path>");
        text = Replace(text, @"/(?:home|Users|data|storage|sdcard|tmp|var)/[^\r\n""<>|]+", "<path>");
        text = Replace(text, @"[\w.+-]{1,128}@[\w.-]{1,128}\.[a-z]{2,24}", "<email>");
        text = Replace(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b\d{15,20}\b", "<id>");
        const string sensitive = @"\b(?:authorization|bearer|token|password|passwd|secret|api[_-]?key|steam[_-]?id|player[_-]?id|user[_-]?name|player[_-]?name)\b";
        text = Replace(text, sensitive + @"[""']?\s*[:=]\s*(?:""[^""\r\n]*""|'[^'\r\n]*')", "<redacted>");
        text = Replace(text, sensitive + @"[""'\s:=]+(?:(?:bearer|basic)\s+)?[^\s,""';]+", "<redacted>");
        text = Replace(text, @"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "");
        return text.Length > limit ? text[..limit] : text;
    }

    // Reserve independent byte budgets so the final v2 JSON fits the 64 KiB endpoint.
    private static string BoundJsonText(string text, int maxBytes)
    {
        while (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(text, JsonOptions)) > maxBytes)
        {
            text = text[..(text.Length * 3 / 4)];
            if (text.Length > 0 && char.IsHighSurrogate(text[^1])) text = text[..^1];
        }
        return text;
    }

    private static bool BoundCoreContext(JsonNode context)
    {
        int bytes = Encoding.UTF8.GetByteCount(context.ToJsonString(JsonOptions));
        bool truncated = false;
        foreach (string name in new[] { "model_sources", "source_mods" })
        {
            if (context[name] is not JsonArray items) continue;
            while (bytes > 8192 && items.Count > 0)
            {
                bytes -= Encoding.UTF8.GetByteCount(items[^1]?.ToJsonString(JsonOptions) ?? "null") + (items.Count > 1 ? 1 : 0);
                items.RemoveAt(items.Count - 1);
                truncated = true;
            }
        }
        if (bytes > 8192) throw new InvalidDataException("Core error context metadata exceeds its budget.");
        return truncated;
    }

    internal static string Normalize(string text)
    {
        text = Replace(text, @"\b[0-9a-f]{8}-[0-9a-f-]{27,}\b|\b0x[0-9a-f]+\b|\b[0-9a-f]{24,}\b|\b\d+\b", "#");
        return Replace(text, @"\s+", " ").Trim();
    }

    internal void Capture(string source, string message, string stack, Func<string>? stackFactory = null)
    {
        lock (_gate)
        {
            var now = _clock();
            if (now - _window >= TimeSpan.FromMinutes(1)) { _window = now; _captures = 0; }
            // Apply before regex/hash work; a failing per-frame callback stays cheap.
            if (++_captures > 120) { _suppressed++; return; }
            message = BoundJsonText(Clean(message, 2048), 4096);
            stack = BoundJsonText(Clean(stackFactory?.Invoke() ?? stack, 8192), 32768);
            if (string.IsNullOrWhiteSpace(message)) return;
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + "\n" + Normalize(message) + "\n" + Normalize(stack))));
            if (!_entries.TryGetValue(key, out var entry))
            {
                if (_entries.Count >= MaxGroups) { _suppressed++; return; }
                entry = new Entry { Source = source, Message = message, Stack = stack, First = now.ToUnixTimeSeconds() };
                _entries.Add(key, entry);
            }
            entry.Count = Math.Min(1000000, entry.Count + 1);
            entry.Last = now.ToUnixTimeSeconds();
        }
    }

    internal long Suppressed { get { lock (_gate) return _suppressed; } }
    internal int GroupCount { get { lock (_gate) return _entries.Count; } }

    internal void Clear()
    {
        lock (_gate) { _entries.Clear(); }
    }

    // A stable report ID + cumulative count makes retries and out-of-order delivery idempotent.
    // Keep counts in memory if persistence is full, so the next snapshot can catch up.
    internal int Persist(Func<string, bool> enqueue)
    {
        List<(Entry Entry, int Count, string Json)> snapshots = new();
        lock (_gate)
        {
            foreach (var entry in _entries.Values.Where(e => e.Count > e.Saved).OrderBy(e => e.Saved).Take(5))
            {
                // Lifetime budget prevents endless new reports during a long running game.
                if (_snapshots + snapshots.Count >= 240) break;
                var report = JsonSerializer.SerializeToNode(new {
                    schema_version = _coreContext == null ? 1 : 2, report_id = entry.Id, session_id = _session,
                    build = _build, mod_version = _version, platform = _platform, os = _os,
                    game_commit = _gameCommit, mods = _mods, mods_truncated = _modsTruncated,
                    source = entry.Source, message = entry.Message, stack = entry.Stack,
                    first_seen = entry.First, last_seen = entry.Last, count = entry.Count,
                }, JsonOptions)!;
                if (_coreContext != null) report["core_telemetry"] = _coreContext.DeepClone();
                string json = report.ToJsonString(JsonOptions);
                snapshots.Add((entry, entry.Count, json));
            }
        }
        int saved = 0;
        foreach (var snapshot in snapshots)
        {
            if (!enqueue(snapshot.Json)) break;
            lock (_gate) { snapshot.Entry.Saved = snapshot.Count; _snapshots++; }
            saved++;
        }
        return saved;
    }
}
