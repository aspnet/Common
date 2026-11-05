# Candidate identity, build and artifact gates

Load with Stage 3 and again before Stage 4. Apply the checkpoint/approval contract in [workflow.md](workflow.md).

## Build and test evidence

Before queuing, record exact source SHA/ref, pipeline parameters, test policy (`runTests` explicitly chosen, not a silent `False` default), expected unit/TFM coverage, integration exclusions and owner. Request queue approval for those inputs. Capture AzDO run URL/ID/number and BAR ID separately. Compare the final build source to the approved SHA and all selected nuspec repository commits.

Check actual pipeline timeline/jobs, test result counts/failures, build logs and verification reports, not only process exit or the top-level green badge. A local `build.cmd` exit 0 with failed tests is a failure. A green official run with tests disabled is build evidence, not unit-test evidence. Record restore/auth blocks, skipped integration tests and unrun TFMs honestly.

Before any retry, inspect parent and child timelines, failed task, published artifacts and idempotence. Distinguish retrying a failed child promotion/job from retrying parent `PublishAssets` or queuing a new build. An existing `ReleaseConfigs` artifact can make a parent replay fail even after the child succeeds. Never delete artifacts or rebuild solely to repaint status; explain existing evidence and obtain a scoped retry/new-build policy decision. A watcher exit, operation result, and independent channel verification are three separate facts.

## Download the correct artifacts

List official build artifacts and inspect `resource.type` first. Use PipelineArtifact tooling for `PipelineArtifact`; for `Container` (`PackageArtifacts`/`PdbArtifacts` can be Container), use the authenticated AzDO Container ZIP/download endpoint or human portal download. Do not repeatedly try pipeline-artifact CLI download for Container resources. Keep tokens in the authenticated human context; never print tokens, credential-bearing URLs or headers.

Record artifact name/type, build ID/source SHA, downloaded archive/package hashes and extraction path. Use `.symbols.nupkg` under the matching PackageArtifacts as the original compiler Portable PDB source; `PdbArtifacts` may contain converted native Windows PDBs. Compare like-for-like package/version/TFM/DLL/PDB identity and hash, never filename alone.

## Prepublication metadata gate (Stage 4a)

Freeze the approved exact package manifest using [package-scope-and-dependency-validation.md](package-scope-and-dependency-validation.md). Inspect **every selected library DLL/TFM's original Portable PDB** from matching symbols packages:

```powershell
& .\.github\agents\release-manager\validate-release\scripts\Test-SourceLink.ps1 `
    -PackageDir "<selected-package-folder>" -Mode Original -SymbolsPackageDir "<symbols-folder>" `
    -ReportPath "<retained-report.json>"
```

Require DLL CodeView GUID/stamp and PDB checksum matching, nonempty usable Source Link mapping, and mapping coverage for nonembedded documents. Record expected no-lib packages explicitly; templates/tools/analyzers without lib DLLs are not library failures but still need dependency validation. An unexpected no-lib package is a manifest review issue.

This check does not require public commit reachability and does not claim URL/checksum success. Empty Source Link payloads must be caught before irreversible publication, not treated as symbol propagation. A record can exist with zero payload bytes. Native PDBs unsupported by the reader/tool are `unsupported-pdb`, not missing symbols. Matching msdl/original bytes with empty metadata point to the artifact, not a promotion failure; they do not identify which build/sign/pack step caused it.

Stop for repair/new candidate on metadata/dependency failure. Only an explicit **validation exception** for identified checks and immutable candidate may authorize proceeding despite failure; record the risk and retain failure status. Do not silently waive, claim full validation, or require a public URL check that is circular with later source reconciliation.

## BAR schema checks

Inspect actual `darc` JSON rather than assuming one version's shape:

- `get-channels` may return a bare array or `{ "channels": [ { "id": ..., "name": ... } ] }`. Unwrap the documented collection, require nonempty entries with id/name, and select exactly the public `.NET <major>` name, not Internal/Eng/Private/Workload.
- `get-build` channels may be strings (channel names) or objects with name/id. Normalize strings directly to names; resolve numeric/object IDs against the validated channel collection. Reject unknown shapes or ambiguous matches visibly, retaining raw nonsecret diagnostic output.

After a monitor/schema error, read build/channel state independently before any mutation. Successful promotion must not be repeated merely because a watcher attempted `channel.id` on a string.
