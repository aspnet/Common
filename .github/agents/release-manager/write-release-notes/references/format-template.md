# Release Notes Format Template

## Monthly release

Use this template for monthly releases (e.g. v10.3.0 → v10.4.0). Confirm version exceptions from the final manifest; "together" does not mean all artifacts use the same version.

```markdown
[Optional preamble — 2–3 sentences summarizing the release theme. May be omitted.]

## Breaking Changes

[If any stable API breaking changes exist — these should be very rare]

1. **Description #PR**
   * Detail of the break
   * Migration guidance

## Experimental API Changes

[Grouped by change type — see experimental-features.md]

### Now Stable
* <Feature Name> APIs are now stable (previously `DIAGID`) #PR

### New Experimental APIs
* New experimental API: <Feature Name> (`DIAGID`) #PR

### Breaking Changes to Experimental APIs
* <Feature Name>: `TypeName` signature changed (experimental under `DIAGID`) #PR

### Removed Experimental APIs
* <Feature Name> experimental APIs removed (was experimental under `DIAGID`) #PR

## What's Changed

### [Area Name — e.g. "AI"]

* Description #PR by @author (co-authored by @user1 @Copilot)
* Description #PR by @author

### [Area Name — e.g. "HTTP Resilience and Diagnostics"]

* Description #PR by @author

### [Area Name — e.g. "Diagnostics, Health Checks, and Resource Monitoring"]

* Description #PR by @author

## Documentation Updates

* Description #PR by @author

## Test Improvements

* Description #PR by @author

## Repository Infrastructure Updates

* Description #PR by @author

## Acknowledgements

* @user made their first contribution in #PR
* @user submitted issue #1234 (resolved by #5678)
* @user1 @user2 @user3 reviewed pull requests

**Full Changelog**: https://github.com/dotnet/extensions/compare/v10.3.0...v10.4.0
```

## Targeted patch release

Use this template when only a subset of packages ships (e.g. v10.3.1). Use the final published
manifest produced under [package scope and dependency validation](../../references/package-scope-and-dependency-validation.md).

```markdown
[Optional preamble — 2–3 sentences summarizing the release theme. May be omitted.]

## Packages in this release

[The complete final published manifest]

| Package | Version |
|---------|---------|
| Package.A | `<version>` |
| Package.B | `<version>` |

## What's Changed

### [Area Name]

* Description #PR by @author

## Acknowledgements

* @user submitted issue #1234 (resolved by #5678)
* @user1 @user2 reviewed pull requests

**Full Changelog**: https://github.com/dotnet/extensions/compare/v10.3.0...v10.3.1
```

## Section rules

1. **Preamble** — optional. If included, summarize the release theme. Suggest a couple of options to
   the user and always offer the option of omitting it.
2. **Packages in this release** — for patch releases only. Table of the complete final published
   manifest with package ID and version only. Omit for full releases when all packages ship at the
   same version with no scope exceptions.
3. **Breaking Changes** — only for stable API breaks (very rare). Omit if none.
4. **Experimental API Changes** — omit if no experimental changes. Omit empty subsections within.
5. **What's Changed** — grouped by area. Order areas by activity (most entries first). Omit areas with no entries.
6. **Documentation Updates** — flat list. Omit if none.
7. **Test Improvements** — flat list. Omit if none.
8. **Repository Infrastructure Updates** — flat list. Omit if none.
9. **Acknowledgements** — always include. Omit empty sub-items.
10. **Full Changelog** — always last. Link to the GitHub compare view.

## PR and issue references

Use the format `#number` for PRs and issues in the same repository. GitHub will auto-link these in release notes. Use full markdown links only for cross-repo references:

- ✅ `#7380` (same repo — GitHub auto-links)
- ✅ `[dotnet/runtime#124264](https://github.com/dotnet/runtime/pull/124264)` (cross-repo)
- ❌ `[#7380](https://github.com/dotnet/extensions/pull/7380)` (unnecessary — same repo)
