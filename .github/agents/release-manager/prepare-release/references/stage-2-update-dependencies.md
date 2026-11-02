# Stage 2 - Update Dependencies

This stage is for the **monthly release**. Servicing releases should follow [stages-1-2-servicing-branch.md](stages-1-2-servicing-branch.md) instead.

Update the internal release branch's product dependencies to the pending .NET 9, .NET 8, and .NET 10 release updates using `darc update-dependencies` against Build Asset Registry (BAR) build IDs.

This stage has three sub-stages. Run them in order, and give each its own commit:

1. Update Dependencies: .NET 9
2. Update Dependencies: .NET 8
3. Update Dependencies: .NET 10

> **Why .NET 9 is first (intentional, not numeric order).** .NET 9 owns the canonical product entries in `Version.Details.xml` and non-suffixed `...Version` properties. Apply it on a clean tree, keeping intended dependency edits and actual darc feed sources. Layer .NET 8 (`...LTSVersion`) and .NET 10 (`...Net10Version`) on top, restoring canonical locations to the .NET 9 values. Discard incidental tooling changes; templating ownership is explicit below.

Review each sub-stage and obtain explicit approval before its commit, following the [stage workflow](../README.md#stage-workflow-monthly-release). There are no automatic commits. Report the commit SHA, then wait for a separate explicit push instruction.

Before committing each sub-stage, review the diff yourself and revert any incidental, non-dependency changes darc introduces (for example, darc sometimes adds a trailing newline to `.config/dotnet-tools.json`). Keep only the intended dependency edits for that sub-stage.

## Prerequisites

- Confirm the working tree is clean and you are on the `stage-release-<major>.<minor>` branch.
- `darc` must be installed and authenticated against BAR:
  - If `darc` is not on the PATH, run `eng/common/darc-init.ps1` (Windows) or `eng/common/darc-init.sh`.
  - On authentication failure, ask the user to run `darc authenticate` in their own terminal. Never ask them to supply tokens in chat or store, echo, or commit tokens.

## Gathering the BAR build IDs (release.dot.net)

The Release Tracker at <https://release.dot.net/releases> is a Blazor WebAssembly app behind Microsoft (MSAL) authentication. The agent cannot reach or render it, so the **user** provides the inputs.

**Collect the inputs for all three sub-stages up front, in a single prompt, before running any `darc` commands.** For each pending release, the user opens it on the Release Tracker, clicks its `{}` artifacts link, and opens `manifests/ReleaseManifest.json`. Accept each one either pasted into the chat or as a local path to a downloaded copy:

Warn explicitly that the tracker may stage **multiple releases for the same version line at once**. Confirm intended month/release date and target versions for every line, not just "latest". Inspect the manifest's actual assets; a plausible filename, valid BAR repository/SHA or latest build alone does not prove it is the intended month's release.

- **.NET 9** -- the pending 9.0 release's `manifests/ReleaseManifest.json`.
- **.NET 8** -- the pending 8.0 release's `manifests/ReleaseManifest.json`.
- **.NET 10** -- the pending 10.0 release's `manifests/ReleaseManifest.json`, or a pasted list of its BAR build IDs from the release page.

Each build entry in a `ReleaseManifest.json` has a `repo` and a `barBuildId`. Read the IDs you need:

- **.NET 9 and .NET 8:** the `barBuildId` for `dotnet-runtime`, `dotnet-aspnetcore`, and `dotnet-efcore`.
- **.NET 10:** all relevant BAR entries, classified into product vs SDK/tooling ownership below. Order matters when VMR builds overwrite the same properties.

Validate every ID with `darc get-build --id <barId>` before applying it, and confirm the repository and commit look correct. If an ID does not resolve, stop and ask the user.

## Validate the release premise before mutation

Present one row per target version line: role/band, old version, intended new version, expected release date/month, manifest identity/hash, BAR root/repo/SHA, SDK/runtime/package asset versions, and evidence that they belong to that release. Crosscheck VMR root and SDK feature band against runtime and actual package versions; SDK `10.0.402` is not a package patch `402`.

For **each version bump on each target line**, raise a concern and seek explicit user confirmation when advancement of the compared component is greater than one (`new - old > 1`), not when the count of skipped intermediate versions is greater than one:

| Version line | Compare | Normal | Requires explicit concern/confirmation |
|---|---|---|---|
| Extensions monthly product | Minor component within major | `10.10.0 -> 10.11.0` | `10.10.0 -> 10.12.0` |
| Extensions servicing product | Patch component within major.minor | `10.11.0 -> 10.11.1` | `10.11.0 -> 10.11.2` |
| Runtime/ASP.NET/package servicing band | Patch within fixed band | `10.0.12 -> 10.0.13` | `10.0.12 -> 10.0.14` |

The user's "minor version" concern includes the third-component servicing example; do not implement it as only SemVer Minor. A major/band transition, downgrade or unexplained package-version mismatch also needs explicit confirmation with evidence. Do not apply arithmetic +1 checks to BAR IDs, SDK feature bands, preview counters or build stamps; validate those against the selected release manifest and their own version policy. Record intentional exceptions per row, not as blanket approval for all lines. Recheck this table when scope/manifests change and before availability polls.

## Sub-stage 1 - Update Dependencies: .NET 9

For each of `dotnet-runtime`, `dotnet-aspnetcore`, and `dotnet-efcore`, using that release's `barBuildId`:

```
darc update-dependencies --id <barBuildId>
```

- `dotnet-efcore` commonly reports `warn: Found no dependencies to update` -- that is expected.
- .NET 9 owns canonical product dependencies: keep intended `eng/Version.Details.xml`, non-suffixed `...Version` entries and actual darc feed additions. Discard incidental tooling, global/common or unrelated changes; "canonical" does not authorize unrelated edits.

Review the changes, then stage and commit:

```
git add eng/Version.Details.xml eng/Versions.props NuGet.config
git commit -m "Update 9.0 dependencies"
```

## Sub-stage 2 - Update Dependencies: .NET 8

For each of `dotnet-runtime`, `dotnet-aspnetcore`, and `dotnet-efcore`, using the 8.0 release's `barBuildId`:

```
darc update-dependencies --id <barBuildId>
```

`dotnet-efcore` commonly reports `warn: Found no dependencies to update`.

Once all three repos are applied, fix up the changes so that only the 8.0 (`...LTSVersion`) entries move:

1. Revert `Version.Details.xml` (the 8.0 update must not rewrite it):

   ```
   git restore --source=HEAD -- eng/Version.Details.xml
   ```

2. `NuGet.config` -- resolve keeping both. Keep the pre-existing internal package sources (added by .NET 9) **and** actual darc-added 8.0 sources; do not synthesize URLs or assume a fixed count. Then:

   ```
   git add NuGet.config
   ```

3. `eng/Versions.props` hand-edit:
   - Move the 8.0 versions darc produced into the matching `...LTSVersion` entries, replacing their previous values (that is, take the updated numbers from the non-suffixed `...Version` entries and write them into the corresponding `...LTSVersion` entries -- "Version>" becomes "LTSVersion>").
   - Revert the changes to the 9.0 (non-suffixed `...Version`) lines.
   - Revert changes to entries that have no 8.0 update: `Microsoft.Bcl.Memory`, `System.Numerics.Tensors`, `System.Memory.Data`.
   - Then `git add eng/Versions.props`.

4. Commit:

   ```
   git commit -m "Update 8.0 dependencies"
   ```

Only `NuGet.config` and `eng/Versions.props` belong in this commit.

## Sub-stage 3 - Update Dependencies: .NET 10

Before applying the .NET 10 IDs, create a property ownership table from their actual asset lists. Multiple VMR builds can overwrite Arcade and templating values:

1. Apply the selected **product/runtime VMR** build first to obtain .NET 10 package dependencies.
2. Apply the selected **SDK/tooling VMR** build last for the intended `MicrosoftDotNetBuildTasksTemplating*Version` values.
3. Capture each build's emitted values/feed provenance before the next update. Product packages retain the product owner's versions even if the SDK update overwrites them; templating retains the SDK owner's approved values. If ownership is ambiguous or manifests disagree, stop for evidence/confirmation rather than choosing numeric latest or iteration order.

Apply each classified ID in that deterministic order:

```
darc update-dependencies --id <barId> --no-coherency-updates
```

Once all IDs are applied, fix up the changes:

1. Revert `Version.Details.xml`:

   ```
   git restore --source=HEAD -- eng/Version.Details.xml
   ```

2. Revert the Arcade / Helix / Build.Tasks.Templating tooling file changes under `eng/common/` (darc rewrites these because Arcade ships in the VMR):

   ```
   git restore --source=HEAD -- eng/common
   ```

3. `NuGet.config` -- resolve keeping both, exactly as in Sub-stage 2: keep the pre-existing and the newly added internal package sources. Then `git add NuGet.config`.

4. `eng/Versions.props` hand-edit:
   - **Keep** the Arcade `MicrosoftDotNetBuildTasksTemplating*Version` entry updates (both the base entry and the `...Net10Version` variant). These stay -- only the `eng/common/` tooling files and `Version.Details.xml` are reverted for Arcade.
   - Move the 10.0 versions darc produced into the matching `...Net10Version` entries, replacing their previous values ("Version>" becomes "Net10Version>").
   - Revert the changes to the 9.0 (non-suffixed `...Version`) lines.
   - Then `git add eng/Versions.props`.

5. Commit:

   ```
   git commit -m "Update 10.0 dependencies"
   ```

Only `NuGet.config` and `eng/Versions.props` belong in this commit -- no `eng/common/` files and no `Version.Details.xml`.

Before each commit, compare final properties to the ownership table: .NET 9 canonical details, .NET 8 LTS overlay, .NET 10 Net10 overlay, both intended templating properties, and the real darc-produced feeds needed for those exact assets. Revert incidental `global.json`, tool manifest, common/Arcade/Helix files and unrelated properties, preserving any pre-existing user work. Retain no guessed feed URLs. Validate evaluated dependency/package versions and restore provenance; record an inaccessible feed as a blocker, not a passed restore. Never run a whole-file restore on a file that also contains unrelated user changes.

## After the stage

Do not push from within this stage. Pushing the `stage-release-<major>.<minor>` branch is a separate, user-directed step. When it happens, resolve the internal remote by its URL rather than assuming a remote name, which varies between clones.
