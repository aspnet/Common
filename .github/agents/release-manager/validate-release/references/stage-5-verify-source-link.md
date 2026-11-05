# Stage 5 - Verify Package Dependencies, Source Link, and Symbols

The publish-release playbook published the packages to nuget.org and promoted/assigned the official release build to the public `.NET <major>` channel. Verify exact-version dependency resolution first. After verified channel assignment, check matching msdl symbols. Full Source Link source URL/checksum validation additionally needs the official source commit public (Stage 6a for monthly releases). The [canonical workflow](../../references/workflow.md) splits these dependencies to avoid a circular Stage 5 -> Stage 6 -> Stage 5 gate.

## Prerequisites

- publish-release is complete: packages published to nuget.org and the official release build assigned/promoted to the public `.NET <major>` channel.
- The `sourcelink` and `dotnet-symbol` global tools (`dotnet tool install -g sourcelink`; `dotnet tool install -g dotnet-symbol`).
- The final published manifest from publish-release is known. It is authoritative for both tracks and
  identifies selected packages separately from dependency-closure packages. For servicing releases,
  do not fall back to the provisional package list in the servicing-prep PR.

## Sub-stage 1 - Verify exact-version dependency resolution

Follow [package scope and dependency validation](../../references/package-scope-and-dependency-validation.md).

Apply the shared invariant to every package in the final published manifest, record its required resolution report. Independent symbol checks may proceed while restore is blocked. Bound propagation retries; a user-approved restore defer/waiver must remain an exception, not a pass.

## Sub-stage 2 - Verify Source Link and symbols

Run the Source Link sweep against the folder containing the published packages for this release scope:

```
& .\.github\agents\release-manager\validate-release\scripts\Test-SourceLink.ps1 `
    -PackageDir "<selected-package-folder>" -Mode Published -MetadataOnly `
    -ReportPath "<retained-msdl-metadata-report.json>"
```

The helper examines **every lib DLL/TFM**, checks DLL/PDB identity/checksum and usable mapping, and retains structured per-asset results/logs. `-MetadataOnly` records `metadata-valid`, not full `valid`. After the build source SHA is public, rerun without `-MetadataOnly` for full URL/checksum validation. Expected no-lib template/tool/analyzer packages must be reviewed against the manifest.

Use the report's explicit results: valid/metadata-valid, no-lib-dll, symbols-not-found, symbol-download-failed (transport/tool error), unsupported-pdb, artifact-mismatch, identity-mismatch, metadata-empty/metadata-invalid, source-url-unreachable, checksum-mismatch, or sourcelink-failed when the tool cannot establish a narrower cause. A failed tool invocation is never "not indexed". The helper requires PowerShell 7.4+; exit 1 indicates failed/partial verification, exit 2 an incomplete run/tooling/input error. Keep the JSON report even when the process fails. Without `-ReportPath`, stdout contains only JSON; failure/partial-sweep diagnostics go to stderr. Use `-Max` only for diagnosis: a partial sweep cannot pass release sign-off. Offline regression coverage is in `scripts/Test-SourceLink.Tests.ps1` (Pester 5+ and a local SDK compiler; no external operations).

Use bounded waits from the canonical workflow. On expiry recheck intended versions, channel assignment, DLL/PDB identities and transport before retrying. Compare original `.symbols.nupkg` Portable PDBs with msdl by GUID/stamp/checksum, length and SHA256; native `PdbArtifacts` are not a like-for-like comparison. A Source Link record with zero payload is broken metadata, not propagation. A website "MissingSymbols" label is a hint, not a diagnosis; a prior successful full sweep is stronger evidence. Do not assert an unverified build/sign/pack cause or claim this helper repairs shipped PDBs.

**Full sign-off requires resolved dependencies and `valid` for every selected library DLL/TFM.** If the human directs other public actions despite failed/unrun checks, record the precise exception and approval, keep the checks failed/unrun, and distinguish that decision from full validation.

## After the stage

After recording dependency and symbol/metadata results:

- **Monthly release:** perform Stage 6a to publish cleaned source history, then finish Stage 5b's full URL/checksum checks. Stage 6b requires the actual release PR merge, not speculative history.
- **Servicing release:** continue with **Stage 7 - Support-Page Follow-up** unless the user explicitly
  requested Stage 6 reconciliation.
