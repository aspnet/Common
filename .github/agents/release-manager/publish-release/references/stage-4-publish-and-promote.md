# Stage 4 - Publish and Promote

After Stage 3 produces the official release build (`PackageArtifacts`), publish the packages to nuget.org and ensure that build is assigned to the public channel so symbols publish to the Microsoft symbol server (msdl).

This stage is shared by both tracks:

- **Monthly release**: Stage 3 official release build comes from `internal/release/<major>.<minor>`.
- **Servicing release**: Stage 3 official release build comes from public `release/<major>.<minor>`.

This stage is operational and produces no commit. Both actions are **irreversible**: publishing is human-gated (the agent prepares and reviews the package set but never runs `dotnet nuget push` itself and never handles API keys), and the channel promotion runs only after the user confirms. Verifying that the published symbols actually landed on msdl is the next playbook -- **validate-release**.

## Prerequisites

- Apply [candidate/artifact/metadata and BAR schema gates](../../references/candidate-validation.md). Stage 4a is required before irreversible publication; full URL validation waits for public source.

- Stage 3 is complete: the official release build succeeded and produced the `PackageArtifacts`.
- For the channel promotion: the `darc` CLI, authenticated to the Build Asset Registry (BAR).

For servicing releases, also require the merged servicing-prep PR description for selected roots and
the approved artifact-derived closure from Stage 3.

## Execution context check

Before running artifact/publish operations, confirm whether this session can access AzDO/BAR with the
required auth context. If access is limited in-session, switch to a terminal handoff flow: provide
exact commands for the user to run outside the session, then continue based on their output.

## Sub-stage 1 - Prepare and publish packages

Publishing is irreversible (a published version cannot be overwritten or truly deleted) and requires nuget.org API keys, so the agent only prepares the set; the user runs the push.

1. Inspect artifact `resource.type`, then download/extract matching official `PackageArtifacts` with the appropriate PipelineArtifact or authenticated Container ZIP mechanism.
   - If in-session artifact download/auth fails, hand off this step to the user's terminal and ask
     them to return the local extracted package folder path.
2. Build and obtain approval for the artifact-derived manifest by following
   [package scope and dependency validation](../../references/package-scope-and-dependency-validation.md).
   Treat any required `Microsoft.Internal.*` package as a blocking packaging error.
3. Validate dependencies and all selected library TFMs' **original** Portable PDB identity/checksum, usable nonempty Source Link mappings and coverage as required by the candidate gate. Record results and source artifact; publication approval is tied to source/build/BAR/manifest hashes.
4. Two nuget.org accounts are involved: almost all packages publish from the **dotnetframework** account; **`Microsoft.Agents.AI.ProjectTemplates`** publishes from the **MicrosoftAgentFramework** account, so it must be pushed separately with that account's key.
5. Order the staged package paths dependency-first where possible. The **user** runs `dotnet nuget push` with the appropriate API key(s) in their terminal (outside
   the agent session). Never run the push, and never handle the API keys.
6. Verify exact nuget.org ID/version listings and record the **final published manifest** for validate-release and notes; attempt consumer restore separately. A listing is not a restore.

### Secure API key entry (user-run helper)

The API key must never be pasted into the agent conversation (it would be captured in session history) and the agent must never run the push or hold the key in a process it controls. Instead, the **user** runs a self-contained helper in their own terminal that captures the key through an editor, pushes the staged packages, then shreds the key file. The key is never echoed and never leaves the user's machine.

The helper opens a throwaway key file in the user's editor with a **wait** flag (so the command blocks until the file is saved and closed), reads the key after close, pushes each staged package, then overwrites and deletes the key file in a `finally` block (so cleanup runs even on error or Ctrl-C). Editors are launched with the flag that blocks until the file is closed:

| Editor | Invocation |
|---|---|
| Sublime Text | `subl --wait <file>` |
| VS Code | `code --wait -- <file>` |
| Notepad | `Start-Process notepad.exe -ArgumentList <file> -Wait` |

```powershell
#requires -Version 5.1
[CmdletBinding()]
param(
  # Absolute paths to the staged .nupkg files to publish.
  [Parameter(Mandatory)][string[]] $Package,
  # Editor used to capture the key. Default is Sublime Text.
  [ValidateSet('subl','code','notepad')][string] $Editor = 'subl',
  [string] $Source = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'
$key = $null
$keyFile = Join-Path ([System.IO.Path]::GetTempPath()) ("nugetkey_{0}.txt" -f ([guid]::NewGuid().ToString('N')))
Set-Content -LiteralPath $keyFile -Value '' -NoNewline

try {
  Write-Host "Opening '$Editor'. Paste the nuget.org API key, save, then CLOSE the file to continue..."
  switch ($Editor) {
    'subl'    { & subl --wait $keyFile }
    'code'    { & code --wait -- $keyFile }
    'notepad' { Start-Process -FilePath notepad.exe -ArgumentList $keyFile -Wait }
  }

  $key = ([string](Get-Content -LiteralPath $keyFile -Raw)).Trim()
  if ([string]::IsNullOrWhiteSpace($key)) { throw "No API key was entered; aborting (nothing pushed)." }

  foreach ($p in $Package) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Package not found: $p" }
    Write-Host "Pushing $([System.IO.Path]::GetFileName($p)) ..."
    dotnet nuget push $p --source $Source --api-key $key
    if ($LASTEXITCODE -ne 0) { throw "push failed for '$p' (exit $LASTEXITCODE)" }
  }
  Write-Host "All packages pushed."
}
finally {
  # Overwrite the key file with random bytes, then delete it. Runs even on error/Ctrl-C.
  if (Test-Path -LiteralPath $keyFile) {
    try {
      $len = [int](Get-Item -LiteralPath $keyFile).Length
      if ($len -gt 0) {
        $rng  = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        $rand = New-Object byte[] $len
        $rng.GetBytes($rand); $rng.Dispose()
        [System.IO.File]::WriteAllBytes($keyFile, $rand)
      }
    } catch { }
    Remove-Item -LiteralPath $keyFile -Force -ErrorAction SilentlyContinue
  }
  if ($key) { $key = $null; Remove-Variable key -ErrorAction SilentlyContinue }
}
```

Notes:
- Pass the staged `.nupkg` paths via `-Package`; select the editor with `-Editor subl|code|notepad` (default `subl`, matching this repository's release engineer's preference).
- The key is passed to `dotnet nuget push` as `--api-key`, so it is briefly visible in local process listings for the duration of each push -- acceptable on the release engineer's own machine. To avoid even that exposure, upload the `.nupkg` files through the nuget.org web UI instead.
- `Microsoft.Agents.AI.ProjectTemplates` uses the separate **MicrosoftAgentFramework** account key; when it is in scope, run the helper a second time with only that package so its key is entered separately.

After a successful push, **delete or regenerate the API key on nuget.org** ([nuget.org/account/apikeys](https://www.nuget.org/account/apikeys)) so the key used for this release is not left valid. The helper shreds the temporary key *file* locally, but only deleting/regenerating the key on nuget.org invalidates it server-side. Guide the user to do this as the final step of Sub-stage 1.

### Terminal handoff expectation

If NuGet API/CLI connectivity fails but the browser works, offer the human nuget.org upload/listing flow immediately. Verify every exact ID/version page (the UI can fall back to a different version), including previews and account ownership. Do not infer "missing" from transport errors. Track upload, listing, API reachability, clean consumer restore, dotnet-public mirror and symbols as separate states. Mirror availability may lag by policy even after all exact listings are public; do not repeat uploads or poll indefinitely. Browser/cached metadata is not consumer restore proof. Follow the canonical bounded monitoring window and recheck the version premise on expiry. If the user explicitly waives/defer postpublish restore, record that exception rather than passing the check; it does not authorize unrelated exceptions or suppress failures.

When handing off Sub-stage 1 commands, provide a copy/paste-ready command block and ask the user to
run it in terminal, then report:

- staged package path(s),
- push output summary,
- post-publish exact-version dependency-resolution results for every published package,
- confirmation that the nuget.org API key was deleted/regenerated.

## Sub-stage 2 - Ensure the official release build is on the public channel

Determine whether manual promotion is required:

- **Monthly release builds from `internal/release/<major>.<minor>`** are typically auto-assigned only to `.NET <major> Internal`; manual promotion to `.NET <major>` is usually required.
- **Servicing builds from public `release/<major>.<minor>`** are typically auto-assigned to `.NET <major>` already; verify before attempting promotion.

1. Identify the official release build's **BAR id** and confirm its commit matches the `repository/commit` embedded in a published `.nuspec`.
2. Find the public channel id: `darc get-channels` -> the entry named exactly `.NET <major>` (no `Internal`/`Eng`/`Private`/`Workload` suffix).
3. Check current channel assignment with `darc get-build --id <bar-id>`:
   - If `.NET <major>` is already present, record that and skip manual promotion.
   - If `.NET <major>` is absent, promote with `darc add-build-to-channel --id <bar-id> --channel ".NET <major>"` **only after explicit user confirmation**.
4. If promotion was required, wait for it to complete, then confirm `.NET <major>` appears in `darc get-build --id <bar-id>`.

Use the shared BAR schema normalization (channels may be strings; `get-channels` may be wrapped). A monitor/schema failure after successful promotion calls for a read-only state check, not another promotion. Inspect child vs parent run/artifact idempotence before any retry; no blind `PublishAssets` replay or artifact deletion.

## After the stage

This stage produces no repository commit. Next, run the **validate-release** playbook: independently
verify exact-version dependency resolution plus Source Link and symbols on msdl (Stage 5), reconcile
the branches (Stage 6), and confirm the support-page listing (Stage 7).
