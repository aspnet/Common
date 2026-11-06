#!/usr/bin/env dotnet
#:package NuGet.Protocol@6.12.1
#:property NuGetAudit=false
#:property NoWarn=IL2026;IL3050
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property ManagePackageVersionsCentrally=false

// Deterministic discovery of the newest coherent Microsoft Agent Framework release on the
// dnceng "dotnet-public" NuGet feed, driven entirely by the feed. Framework-general: it carries
// no consumer-specific (e.g. project-template) knowledge. It emits the release version, its
// date-stamp, and every Microsoft.Agents.AI* family package's version at that release (plus each
// package's newest-overall and newest-stable). Consumers -- currently the aiagent-webapi project
// template worker -- map this signal onto their own package subset and files.
//
// The family is enumerated from the feed (PackageSearchResource, which sends semVerLevel=2.0.0 so
// preview/alpha-only packages are visible) unioned with a known-id seed so a search hiccup can
// never silently drop a package. AutoCompleteResource is NOT used: AzDO feeds expose no
// SearchAutocompleteService, so IdStartsWith throws at runtime. Version selection uses NuGetVersion
// / VersionComparer for correct SemVer 2.0 ordering, so the feed's return order is irrelevant and
// the anomalous 0.0.1-preview.* entry (present across most of the family) can never win.
//
// When an output-file path is passed as the first argument the JSON array is written there (so the
// "dotnet run" build/restore output on stdout never contaminates the capture); otherwise it is
// written to stdout. Notices/warnings/errors go to stderr as GitHub Actions workflow commands.

using System.Text.Json;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

const string Channel = "dotnet-public";
const string FamilyPrefix = "Microsoft.Agents.AI";
const string Anchor = "Microsoft.Agents.AI";                       // always publishes a stable release
const string ExcludeId = "Microsoft.Agents.AI.ProjectTemplates";  // consumer package, not framework

// Known family ids (determinism seed; search adds any new ones automatically).
string[] seed =
[
    "Microsoft.Agents.AI", "Microsoft.Agents.AI.Abstractions", "Microsoft.Agents.AI.OpenAI",
    "Microsoft.Agents.AI.Workflows", "Microsoft.Agents.AI.Workflows.Generators",
    "Microsoft.Agents.AI.DevUI", "Microsoft.Agents.AI.Hosting",
    "Microsoft.Agents.AI.Foundry", "Microsoft.Agents.AI.Foundry.Hosting",
    "Microsoft.Agents.AI.Hosting.OpenAI",
];

static string FeedIndex(string channel) =>
    $"https://pkgs.dev.azure.com/dnceng/public/_packaging/{channel}/nuget/v3/index.json";
static void Notice(string m) => Console.Error.WriteLine($"::notice::{m}");
static void Warn(string m) => Console.Error.WriteLine($"::warning::{m}");
static void Fail(string m)
{
    Console.Error.WriteLine($"::error::{m}");
    Environment.Exit(1);
}

var repo = Repository.Factory.GetCoreV3(FeedIndex(Channel));
var cache = new SourceCacheContext { NoCache = true };   // see just-published builds immediately
var log = NullLogger.Instance;
var ct = CancellationToken.None;

// ---- enumerate the family: search (semVerLevel=2.0.0, page-by-count) UNION seed --------------
async Task<SortedSet<string>> DiscoverFamily()
{
    var ids = new SortedSet<string>(seed, StringComparer.OrdinalIgnoreCase);
    try
    {
        var search = await repo.GetResourceAsync<PackageSearchResource>();   // NOT AutoCompleteResource (throws on AzDO)
        var filter = new SearchFilter(includePrerelease: true);
        int skip = 0, take = 100;
        while (true)
        {
            var page = (await search.SearchAsync(FamilyPrefix, filter, skip, take, log, ct)).ToList();
            foreach (var r in page)
                if (r.Identity.Id.StartsWith(FamilyPrefix, StringComparison.OrdinalIgnoreCase))
                    ids.Add(r.Identity.Id);
            if (page.Count < take) break;   // AzDO totalHits is always "0"; page by returned count
            skip += take;
        }
    }
    catch (Exception ex)
    {
        Warn($"feed search unavailable ({ex.GetType().Name}: {ex.Message}); falling back to seed ids only.");
    }
    ids.RemoveWhere(id => id.Equals(ExcludeId, StringComparison.OrdinalIgnoreCase));
    return ids;
}

// All published versions of a package on the feed, minus the anomalous 0.0.1-preview.* entry.
// Retries a few transient feed errors before treating the package as having no builds.
async Task<IReadOnlyList<NuGetVersion>> AllVersions(string id)
{
    Exception? last = null;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            var res = await repo.GetResourceAsync<FindPackageByIdResource>();
            var vs = await res.GetAllVersionsAsync(id, cache, log, ct);
            return vs.Where(v => v.Major > 0).ToList();
        }
        catch (Exception ex)
        {
            last = ex;
            if (attempt < 3) await Task.Delay(2000);
        }
    }
    Warn($"could not read versions for '{id}' after 3 attempts: {last?.Message}");
    return [];
}

var family = await DiscoverFamily();
var versionsById = new Dictionary<string, IReadOnlyList<NuGetVersion>>(StringComparer.OrdinalIgnoreCase);
foreach (var id in family)
    versionsById[id] = await AllVersions(id);

// Newest by SemVer 2.0 order, or null when the sequence is empty -- e.g. a transient feed/API
// error left a package (or the anchor) with no versions. Explicit LastOrDefault-after-ordering so
// the empty case is obvious and callers emit a controlled Fail()/null, rather than relying on the
// reference-type Enumerable.Max() returning null on empty.
static NuGetVersion? Newest(IEnumerable<NuGetVersion> vs) => vs.OrderBy(v => v).LastOrDefault();

// ---- release_version = newest STABLE of the anchor ------------------------------------------
var anchorVersions = versionsById.TryGetValue(Anchor, out var av) ? av : [];
var release = Newest(anchorVersions.Where(v => !v.IsPrerelease));
if (release is null)
    Fail($"No stable {Anchor} version found on {Channel}; aborting rather than emitting an empty signal.");

bool AtRelease(NuGetVersion v) =>
    v.Major == release!.Major && v.Minor == release.Minor && v.Patch == release.Patch;

static string Tier(NuGetVersion v) =>
    !v.IsPrerelease ? "stable"
    : v.Release.StartsWith("alpha", StringComparison.OrdinalIgnoreCase) ? "alpha"
    : v.Release.StartsWith("preview", StringComparison.OrdinalIgnoreCase) ? "preview"
    : v.Release.Split('.')[0].ToLowerInvariant();

// ---- per-package: newest at the coherent release (any tier), newest overall, newest stable ---
var packages = new SortedDictionary<string, object?>(StringComparer.Ordinal);
string? releaseDate = null;
foreach (var (id, vs) in versionsById)
{
    var atRelease = Newest(vs.Where(AtRelease));
    var newest = Newest(vs);
    var newestStable = Newest(vs.Where(v => !v.IsPrerelease));

    if (releaseDate is null && atRelease is { IsPrerelease: true })
    {
        var parts = atRelease.Release.Split('.');   // e.g. "preview", "260703", "1"
        if (parts.Length >= 2) releaseDate = parts[1];
    }

    packages[id] = new Dictionary<string, string?>
    {
        ["tier"] = atRelease is null ? null : Tier(atRelease),
        ["at_release"] = atRelease?.ToNormalizedString(),
        ["latest"] = newest?.ToNormalizedString(),
        ["latest_stable"] = newestStable?.ToNormalizedString(),
    };
}

Notice($"Agent Framework release {release} (date {releaseDate ?? "n/a"}) across {family.Count} packages on {Channel}.");

var signal = new Dictionary<string, object?>
{
    ["source_feed"] = Channel,
    ["release_version"] = release!.ToNormalizedString(),   // e.g. "1.13.0"
    ["release_date"] = releaseDate,                        // e.g. "260703"
    ["packages"] = packages,
};

// Emit a single-element array so the orchestrator's matrix fans out over one target (the release).
var json = JsonSerializer.Serialize(new[] { signal });
if (args.Length > 0)
    File.WriteAllText(args[0], json);
else
    Console.WriteLine(json);
