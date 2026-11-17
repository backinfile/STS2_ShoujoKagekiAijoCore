using ShoujoKageki.Core.Bootstrap;

if (args.Length != 1) throw new ArgumentException("Pass the complete Core package directory.");
var candidate = BundleSelection.Validate(args[0], "Local:official");
var requirements = new[] { new CoreRequirements { ModId = "CharacterA", RequiredFeatures = ["shine@1"] } };
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or InvalidOperationException or FormatException) { checks++; return; }
    throw new InvalidOperationException(name);
}
BundleCandidate Select(params BundleCandidate[] candidates) => BundleSelection.Select(candidates, requirements, "0.107.1", "3.4.8", "1.0.0");
Check(ReferenceEquals(Select(candidate), candidate), "single complete bundle");
Check(ReferenceEquals(BundleSelection.Select([candidate], requirements, "0.113.0", "3.4.8", "1.0.0"), candidate), "v0.113.0 selects the current beta runtime");
Check(ReferenceEquals(BundleSelection.Select([candidate], requirements, "0.113.1", "3.4.8", "1.0.0"), candidate), "new beta patch attempts the installed runtime");
Check(BundleSelection.SelectTarget(candidate.Bundle, "0.111.0")?.Name == "stable", "older game does not load a newer beta baseline");
var identical = candidate with { Root = "workshop-copy", Source = "Workshop:copy" };
Check(Select(identical, candidate).Source == candidate.Source, "identical copies converge by stable source identity");
var release = Version.Parse(candidate.Bundle.ReleaseVersion.Split('-')[0].TrimStart('v'));
var nextRelease = new Version(release.Major, release.Minor, release.Build + 1).ToString();
var newer = candidate with { Bundle = candidate.Bundle with { ReleaseVersion = nextRelease, ResourceVersion = nextRelease }, Fingerprint = "newer" };
Check(ReferenceEquals(Select(candidate, newer), newer), "highest common compatible release");
var missingFeature = newer with { Bundle = newer.Bundle with { Features = ["music@1"] } };
Check(ReferenceEquals(Select(candidate, missingFeature), candidate), "newer missing required feature falls back");
var betaOnly = newer with { Bundle = newer.Bundle with { Targets = new() { ["beta"] = newer.Bundle.Targets["beta"] } } };
Check(ReferenceEquals(Select(candidate, betaOnly), candidate), "newer target mismatch falls back");
var conflicting = candidate with { Root = "conflicting-copy", Source = "Workshop:conflicting", Fingerprint = "conflicting" };
var selections = Enumerable.Range(0, 128).Select(_ => Select(candidate, conflicting)).ToArray();
Check(selections.All(x => ReferenceEquals(x, candidate) || ReferenceEquals(x, conflicting)), "same release content differences select a compatible candidate without error");
Console.WriteLine($"Conflict selection samples: official={selections.Count(x => ReferenceEquals(x, candidate))}, alternate={selections.Count(x => ReferenceEquals(x, conflicting))}");
Check(ReferenceEquals(Select(candidate, missingFeature with { Bundle = missingFeature.Bundle with { ReleaseVersion = candidate.Bundle.ReleaseVersion } }), candidate), "same release differences filter incompatible features before selection");
Check(ReferenceEquals(Select(candidate, conflicting, newer), newer), "older conflicting copies do not block the preferred release");
Check(ReferenceEquals(Select(candidate, betaOnly with { Bundle = betaOnly.Bundle with { ReleaseVersion = candidate.Bundle.ReleaseVersion } }), candidate), "same release differences filter incompatible game baselines before selection");
Check(ReferenceEquals(BundleSelection.Select([candidate], requirements, "0.112.0", "3.4.8", "1.0.0"), candidate), "unknown intermediate version attempts the lower baseline");
Reject(() => BundleSelection.Select([candidate], requirements, "0.107.0", "3.4.8", "1.0.0"), "game older than every baseline rejected");
foreach (var version in new[] { "0.113.1", "0.114.0", "1.0.0" })
    Check(BundleSelection.SelectTarget(candidate.Bundle, version)?.Name == "beta", "newer game selects beta baseline: " + version);
Check(BundleSelection.SelectTarget(candidate.Bundle, "0.107.2")?.Name == "stable", "new stable patch keeps stable baseline");
var futureBaseline = newer with { Bundle = newer.Bundle with { Targets = new(candidate.Bundle.Targets) {
    ["beta"] = candidate.Bundle.Targets["beta"] with { MinimumGameVersion = "0.114.0", MaximumGameVersion = "0.114.0" }
} } };
Check(ReferenceEquals(BundleSelection.Select([futureBaseline, candidate], requirements, "0.113.1", "3.4.8", "1.0.0"), candidate), "closest applicable baseline takes priority over newer Core release");
Reject(() => BundleSelection.Select([candidate], requirements, "0.107.1", "3.0.0", "1.0.0"), "BaseLib minimum enforced");
Reject(() => BundleSelection.Select([candidate], requirements, "0.107.1", "3.4.8", $"{release.Major + 1}.0.0"), "role release minimum enforced");
Reject(() => BundleSelection.Select([candidate], [new CoreRequirements { ModId = "V2", ApiMajor = 2 }], "0.107.1", "3.4.8", "1.0.0"), "API major mismatch");
Reject(() => BundleSelection.ResolvePath(args[0], "../escape.dll"), "path traversal rejected");
Check(SemVersion.Parse("1.0.0").CompareTo(SemVersion.Parse("1.0.0-beta.10")) > 0, "stable after prerelease");
Check(SemVersion.Parse("1.0.0-beta.10").CompareTo(SemVersion.Parse("1.0.0-beta.2")) > 0, "numeric prerelease ordering");
Check(SemVersion.Parse("1.0.0-beta.2").CompareTo(SemVersion.Parse("1.0.0-beta.2.1")) < 0, "longer prerelease ordering");
var scratch = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "integrity-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
foreach (var file in Directory.EnumerateFiles(args[0], "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(args[0], file);
    var output = Path.Combine(scratch, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.Copy(file, output);
}
try
{
    Check(BundleSelection.ValidateStrict(scratch, "strict").Fingerprint == candidate.Fingerprint, "strict official package");
    var extra = Path.Combine(scratch, "unexpected.txt");
    File.WriteAllText(extra, "extra");
    Reject(() => BundleSelection.ValidateStrict(scratch, "strict"), "strict rejects unlisted files");
    File.Delete(extra);
    var runtime = Path.Combine(scratch, candidate.Bundle.Targets["beta"].Assembly);
    File.AppendAllText(runtime, "tampered");
    Reject(() => BundleSelection.Validate(scratch, "Workshop:tampered"), "unselected target runtime is also verified");
}
finally { Directory.Delete(scratch, true); }
Console.WriteLine($"Foundation checks passed: {checks}");
