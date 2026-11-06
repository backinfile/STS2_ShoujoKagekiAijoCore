using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

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
        var required = new[] { bundle.ApiAssembly, bundle.ResourcePack, "ShoujoKagekiAijoCore.dll", "ShoujoKagekiAijoCore.json" }.Concat(bundle.Targets.Values.Select(x => x.Assembly));
        foreach (var path in required)
            if (!bundle.Files.ContainsKey(path)) throw new InvalidDataException($"Missing bundle digest for {path}.");
        foreach (var entry in bundle.Files)
        {
            var path = ResolvePath(root, entry.Key);
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(entry.Value, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Core file checksum failed: {entry.Key}.");
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(ResolvePath(root, "ShoujoKagekiAijoCore.json")));
        if (manifest.RootElement.GetProperty("id").GetString() != "ShoujoKagekiAijoCore"
            || manifest.RootElement.GetProperty("version").GetString() != bundle.ReleaseVersion)
            throw new InvalidDataException("Core manifest and bundle release identity differ.");
        var api = AssemblyName.GetAssemblyName(ResolvePath(root, bundle.ApiAssembly));
        if (api.Name != "ShoujoKagekiAijoCore.Api.V1" || api.Version != new Version(1, 0, 0, 0))
            throw new InvalidDataException("Unexpected Core API assembly identity.");
        foreach (var (name, target) in bundle.Targets)
        {
            if (name is not ("stable" or "beta")) throw new InvalidDataException($"Unknown game target {name}.");
            var asm = AssemblyName.GetAssemblyName(ResolvePath(root, target.Assembly));
            var expected = $"ShoujoKagekiAijoCore.Runtime.{(name == "stable" ? "Stable" : "Beta")}";
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

    public static BundleCandidate Select(IEnumerable<BundleCandidate> candidates, IReadOnlyList<CoreRequirements> requirements,
        string gameVersion, string baseLibVersion, string minimumRelease)
    {
        var complete = candidates.ToArray();
        foreach (var release in complete.GroupBy(x => x.Bundle.ReleaseVersion))
            if (release.Select(x => x.Fingerprint).Distinct().Count() > 1)
                throw new InvalidDataException($"Core {release.Key} 存在同版本内容冲突，请重新安装官方发行包。");
        var game = SemVersion.Parse(gameVersion);
        var baselib = SemVersion.Parse(baseLibVersion);
        var compatible = complete.Where(x => x.Bundle.ApiMajor == 1
            && SemVersion.Parse(x.Bundle.ReleaseVersion).CompareTo(SemVersion.Parse(minimumRelease)) >= 0
            && baselib.CompareTo(SemVersion.Parse(x.Bundle.BaseLibMinimumVersion)) >= 0
            && x.Bundle.Targets.Values.Any(t => game.CompareTo(SemVersion.Parse(t.MinimumGameVersion)) >= 0 && game.CompareTo(SemVersion.Parse(t.MaximumGameVersion)) <= 0)
            && requirements.All(r => r.Format == 1 && r.ApiMajor == x.Bundle.ApiMajor
                && SemVersion.Parse(x.Bundle.ApiVersion).CompareTo(SemVersion.Parse(r.MinimumApiVersion)) >= 0
                && r.RequiredFeatures.All(f => x.Bundle.Features.Contains(f, StringComparer.Ordinal))))
            .OrderByDescending(x => SemVersion.Parse(x.Bundle.ReleaseVersion)).ThenBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.Root, StringComparer.Ordinal).ToArray();
        if (compatible.Length == 0) throw new InvalidOperationException("没有共同兼容的 Core。请更新角色包，检查游戏版本、BaseLib、API 与必需功能。" +
            string.Join(" ", requirements.Select(r => $"{r.ModId}: API {r.ApiMajor}/{r.MinimumApiVersion}, {string.Join(',', r.RequiredFeatures)}")));
        var selected = compatible[0];
        if (compatible.Any(x => x.Bundle.ReleaseVersion == selected.Bundle.ReleaseVersion && x.Fingerprint != selected.Fingerprint))
            throw new InvalidDataException($"Core {selected.Bundle.ReleaseVersion} 存在同版本内容冲突，请重新安装官方发行包。");
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
