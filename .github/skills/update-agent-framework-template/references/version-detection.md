# Version detection (the `dotnet-public` feed signal)

`.github/scripts/agent-framework-discover.cs` is the authoritative detector. It is a .NET 10
file-based app (`dotnet run agent-framework-discover.cs -- <out.json>`) that uses the NuGet client
SDK (`NuGet.Protocol` + `NuGet.Versioning`).

## Feed

- Service index: `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json`
  (unauthenticated). This is the same feed the template's package restore uses, so any version
  detected here is guaranteed restorable by CI -- eliminating a nuget.org-vs-feed mirror race.

## Family enumeration

- Use `PackageSearchResource.SearchAsync("Microsoft.Agents.AI", new SearchFilter(includePrerelease: true), ...)`.
  The client SDK sends `semVerLevel=2.0.0`, so preview/alpha-only packages are returned; a raw `curl`
  on the `query2` endpoint without that parameter silently omits them.
- Page by returned count (`page.Count < take`) -- Azure DevOps always reports `totalHits: "0"`.
- Union the search results with a curated seed id list so a search hiccup never drops a known package;
  exclude `Microsoft.Agents.AI.ProjectTemplates` (the consumer's own package, not a framework dep).
- Do **not** use `AutoCompleteResource`: AzDO exposes no `SearchAutocompleteService`, so
  `IdStartsWith` throws at runtime even though `GetResourceAsync` returns non-null.

## Version selection

- `FindPackageByIdResource.GetAllVersionsAsync(id, ...)`, then select with `NuGetVersion`/
  `VersionComparer` -- order-independent and SemVer-2 correct, so the feed's descending return order
  is irrelevant.
- Exclude the anomalous `0.0.1-preview.*` entry (filter `Major > 0`). `NuGetVersion.Max()` never
  selects it anyway.
- `release_version` = newest **stable** of the anchor `Microsoft.Agents.AI`.
- Per package: `at_release` = newest version whose `major.minor.patch` matches `release_version`
  (any tier). This is what the template pins. Also report `latest_stable` -- note that
  `Microsoft.Agents.AI.Foundry`'s newest stable (`1.5.0`) lags its `at_release` preview, so
  "newest stable per package" is the wrong selector.

## Output

A single-element JSON array (so the orchestrator matrix fans out over one target) whose element is the
framework signal: `source_feed`, `release_version`, `release_date`, and a `packages` map of
`{ id: { tier, at_release, latest, latest_stable } }`.
