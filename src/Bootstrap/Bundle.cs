#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoujoKageki.Core.Bootstrap;

public sealed record BundleTarget
{
    public string MinimumGameVersion { get; init; } = "";
    public string MaximumGameVersion { get; init; } = "";
    public string Assembly { get; init; } = "";
}
public sealed record CoreBundle
{
    public int Format { get; init; }
    public int BootstrapProtocol { get; init; }
    public string ReleaseVersion { get; init; } = "";
    public int ApiMajor { get; init; }
    public string ApiVersion { get; init; } = "";
    public string ApiAssembly { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyApiAssembly { get; init; }
    public string BaseLibMinimumVersion { get; init; } = "";
    public string ResourceVersion { get; init; } = "";
    public string ResourcePack { get; init; } = "";
    public int SaveProtocol { get; init; }
    public int NetworkProtocol { get; init; }
    public string[] Features { get; init; } = [];
    public Dictionary<string, BundleTarget> Targets { get; init; } = [];
    public Dictionary<string, string> Files { get; init; } = [];
}
public sealed record CoreRequirements
{
    public int Format { get; init; } = 1;
    public string ModId { get; init; } = "";
    public int ApiMajor { get; init; } = 1;
    public string MinimumApiVersion { get; init; } = "1.0.0";
    public string[] RequiredFeatures { get; init; } = [];
    public string? CharacterFamily { get; init; }
    public string[] OptionalFeatures { get; init; } = [];
}
public sealed record BundleCandidate(string Root, string Source, CoreBundle Bundle, string Fingerprint);
public sealed record BundleTargetMatch(string Name, BundleTarget Target);

public static class BundleSelection
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Empty metadata {Path.GetFileName(path)}.");

    public static string ResolvePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Bundle paths must be relative.");
        root = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Bundle path escapes release directory: {relative}.");
        for (var current = full; current != root; current = Path.GetDirectoryName(current)!)
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Bundle path contains a link: {relative}.");
        return full;
    }

    public static BundleCandidate Validate(string root, string source)
    {
        var bundle = Read<CoreBundle>(Path.Combine(root, "core.bundle"));
        if (bundle.Format != 1 || bundle.BootstrapProtocol != 1 || bundle.ApiMajor != 1 || bundle.SaveProtocol < 1 || bundle.NetworkProtocol < 1)
            throw new InvalidDataException("Unsupported Core bundle, API, save or bootstrap protocol.");
        _ = SemVersion.Parse(bundle.ReleaseVersion);
        _ = SemVersion.Parse(bundle.ApiVersion);
        _ = SemVersion.Parse(bundle.BaseLibMinimumVersion);
        if (bundle.ResourceVersion != bundle.ReleaseVersion || bundle.Targets.Count == 0)
            throw new InvalidDataException("Core resource release or game targets are missing.");
        var required = new[] { bundle.ApiAssembly, bundle.ResourcePack, "ShoujoKagekiCore.dll", "ShoujoKagekiCore.json" }.Concat(bundle.Targets.Values.Select(x => x.Assembly));
        foreach (var path in required)
            if (!bundle.Files.ContainsKey(path)) throw new InvalidDataException($"Missing bundle digest for {path}.");
        foreach (var entry in bundle.Files)
        {
            var path = ResolvePath(root, entry.Key);
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(entry.Value, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Core file checksum failed: {entry.Key}.");
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(ResolvePath(root, "ShoujoKagekiCore.json")));
        if (manifest.RootElement.GetProperty("id").GetString() != "ShoujoKagekiCore"
            || manifest.RootElement.GetProperty("version").GetString() != bundle.ReleaseVersion)
            throw new InvalidDataException("Core manifest and bundle release identity differ.");
        var api = AssemblyName.GetAssemblyName(ResolvePath(root, bundle.ApiAssembly));
        if (api.Name != "ShoujoKagekiCore.Api.V1" || api.Version != new Version(1, 0, 0, 0))
            throw new InvalidDataException("Unexpected Core API assembly identity.");
        if (bundle.LegacyApiAssembly is { } legacyApi)
        {
            if (!bundle.Files.ContainsKey(legacyApi)) throw new InvalidDataException("Missing legacy API digest.");
            var legacy = AssemblyName.GetAssemblyName(ResolvePath(root, legacyApi));
            if (legacy.Name != "ShoujoKagekiAijoCore.Api.V1" || legacy.Version != api.Version)
                throw new InvalidDataException("Unexpected legacy Core API assembly identity.");
        }
        foreach (var (name, target) in bundle.Targets)
        {
            var variant = name switch
            {
                "stable" => "Stable",
                "main" => "Main",
                "beta" => "Beta",
                _ => throw new InvalidDataException($"Unknown game target {name}.")
            };
            var asm = AssemblyName.GetAssemblyName(ResolvePath(root, target.Assembly));
            var expected = $"ShoujoKagekiCore.Runtime.{variant}";
            if (asm.Name != expected) throw new InvalidDataException($"Unexpected runtime identity for {name}.");
            if (SemVersion.Parse(target.MinimumGameVersion).CompareTo(SemVersion.Parse(target.MaximumGameVersion)) > 0)
                throw new InvalidDataException($"Inverted game range for {name}.");
        }
        using var pack = File.OpenRead(ResolvePath(root, bundle.ResourcePack));
        Span<byte> magic = stackalloc byte[4];
        if (pack.Read(magic) != 4 || !magic.SequenceEqual("GDPC"u8)) throw new InvalidDataException("Core resource pack is invalid.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bundle, JsonOptions))));
        return new BundleCandidate(Path.GetFullPath(root), source, bundle, fingerprint);
    }

    public static BundleCandidate ValidateStrict(string root, string source)
    {
        var candidate = Validate(root, source);
        foreach (var entry in EnumeratePackageEntries(candidate.Root))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Core package contains a link.");
            if (Directory.Exists(entry)) continue;
            var relative = Path.GetRelativePath(candidate.Root, entry).Replace('\\', '/');
            if (relative != "core.bundle" && !candidate.Bundle.Files.ContainsKey(relative))
                throw new InvalidDataException($"Core package contains an unlisted file: {relative}");
        }
        return candidate;
    }

    private static IEnumerable<string> EnumeratePackageEntries(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            // Check before descending: links may leave the package or form a cycle.
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Core package contains a link.");
            yield return entry;
            if (Directory.Exists(entry))
                foreach (var nested in EnumeratePackageEntries(entry)) yield return nested;
        }
    }

    public static BundleTargetMatch? SelectTarget(CoreBundle bundle, string gameVersion, string? compatibleTarget = null)
    {
        var game = SemVersion.Parse(gameVersion);
        return bundle.Targets.Where(x => (compatibleTarget == null || x.Key == compatibleTarget)
                && game.CompareTo(SemVersion.Parse(x.Value.MinimumGameVersion)) >= 0)
            .OrderByDescending(x => SemVersion.Parse(x.Value.MinimumGameVersion))
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new BundleTargetMatch(x.Key, x.Value)).FirstOrDefault();
    }

    public static BundleCandidate Select(IEnumerable<BundleCandidate> candidates, IReadOnlyList<CoreRequirements> requirements,
        string gameVersion, string baseLibVersion, string minimumRelease, string? compatibleTarget = null)
    {
        var complete = candidates.ToArray();
        var conflictingReleases = complete.GroupBy(x => x.Bundle.ReleaseVersion)
            .Where(release => release.Select(x => x.Fingerprint).Distinct().Skip(1).Any())
            .Select(release => release.Key).ToHashSet(StringComparer.Ordinal);
        var baselib = SemVersion.Parse(baseLibVersion);
        var selected = complete.Select(candidate => new { Candidate = candidate, Target = SelectTarget(candidate.Bundle, gameVersion, compatibleTarget) })
            .Where(x => x.Target != null && x.Candidate.Bundle.ApiMajor == 1
                && SemVersion.Parse(x.Candidate.Bundle.ReleaseVersion).CompareTo(SemVersion.Parse(minimumRelease)) >= 0
                && baselib.CompareTo(SemVersion.Parse(x.Candidate.Bundle.BaseLibMinimumVersion)) >= 0
                && requirements.All(r => r.Format == 1 && r.ApiMajor == x.Candidate.Bundle.ApiMajor
                    && SemVersion.Parse(x.Candidate.Bundle.ApiVersion).CompareTo(SemVersion.Parse(r.MinimumApiVersion)) >= 0
                    && r.RequiredFeatures.All(f => x.Candidate.Bundle.Features.Contains(f, StringComparer.Ordinal))))
            .OrderByDescending(x => SemVersion.Parse(x.Target!.Target.MinimumGameVersion))
            .ThenByDescending(x => SemVersion.Parse(x.Candidate.Bundle.ReleaseVersion))
            .ThenBy(x => conflictingReleases.Contains(x.Candidate.Bundle.ReleaseVersion) ? Random.Shared.NextDouble() : 0)
            .ThenBy(x => x.Candidate.Source, StringComparer.Ordinal).ThenBy(x => x.Candidate.Root, StringComparer.Ordinal)
            .Select(x => x.Candidate).FirstOrDefault();
        if (selected == null) throw new InvalidOperationException("没有共同兼容的 Core。请更新 Core 工坊订阅或 Karen 包，检查游戏版本、BaseLib、API 与必需功能。" +
            string.Join(" ", requirements.Select(r => $"{r.ModId}: API {r.ApiMajor}/{r.MinimumApiVersion}, {string.Join(',', r.RequiredFeatures)}")));
        return selected;
    }
}

public sealed class SemVersion : IComparable<SemVersion>
{
    private readonly int[] _numbers;
    private readonly string[] _prerelease;
    private SemVersion(int[] numbers, string[] prerelease) { _numbers = numbers; _prerelease = prerelease; }
    public static SemVersion Parse(string value)
    {
        var pieces = value.Trim().TrimStart('v').Split('+')[0].Split('-', 2);
        var numbers = pieces[0].Split('.');
        if (numbers.Length != 3 || numbers.Any(x => !int.TryParse(x, out var n) || n < 0)) throw new FormatException($"Invalid semantic version {value}.");
        return new SemVersion(numbers.Select(int.Parse).ToArray(), pieces.Length == 2 ? pieces[1].Split('.') : []);
    }
    public int CompareTo(SemVersion? other)
    {
        if (other == null) return 1;
        for (var i = 0; i < 3; i++) { var c = _numbers[i].CompareTo(other._numbers[i]); if (c != 0) return c; }
        if (_prerelease.Length == 0 || other._prerelease.Length == 0) return other._prerelease.Length.CompareTo(_prerelease.Length);
        for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            var a = _prerelease[i]; var b = other._prerelease[i];
            var an = int.TryParse(a, out var av); var bn = int.TryParse(b, out var bv);
            var c = an && bn ? av.CompareTo(bv) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a, b);
            if (c != 0) return c;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }
}
