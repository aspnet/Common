---
name: investigate-npm-feed-failures
description: Diagnose npm E401 and missing-package failures involving the public dotnet Azure Artifacts feed, including false-green builds and upstream versions not yet saved in the feed.
---

# Investigate npm Feed Failures

Use this skill when an extensions build reports npm `E401`, fails while building the Azure DevOps reporting plugin, or succeeds despite an npm error.

## Understand the Feed Model

`dotnet-public-npm` is a public Azure Artifacts feed with an npmjs upstream:

- Anonymous users can download package versions already saved in the feed.
- Only an authenticated identity with **Feed and Upstream Reader (Collaborator)** or higher permission can cause a previously unsaved upstream version to be imported.
- Package metadata can advertise an upstream version before its tarball has been saved locally. Do not treat metadata visibility as proof that anonymous restore will succeed.

Microsoft documents this behavior in [Use upstream sources in a public feed](https://learn.microsoft.com/azure/devops/artifacts/how-to/public-feeds-upstream-sources#restore-packages).

## Investigation Procedure

### 1. Determine whether the npm restore ran

The Azure DevOps reporting plugin build is inside the Windows-only section of `eng/pipelines/templates/BuildAndTest.yml`. A green Ubuntu leg does not prove that npm restore succeeded; Ubuntu does not execute this step.

### 2. Search every relevant build log

Search for:

- `E401`
- `Unable to authenticate`
- `npm error`
- `Cannot find path` followed by `node_modules`

The normal npm console output usually does not identify the failed package. The referenced agent-local `*-debug-0.log` contains the URL, but it is not normally uploaded.

If the script ignores npm's native exit code, classify a later `Copy-Item` failure as secondary. A successful task containing E401 is a false green and may have packaged stale `node_modules`.

### 3. Reproduce anonymously with verbose logging

Prevent user and global npm credentials from changing the result:

```powershell
$env:NPM_CONFIG_USERCONFIG = "$env:TEMP\missing-user-npmrc"
$env:NPM_CONFIG_GLOBALCONFIG = "$env:TEMP\missing-global-npmrc"
npm ci --omit=dev --loglevel verbose
```

Inspect the debug log for:

```text
http fetch GET 401 <tarball-url>
verbose pkgid <package>@<tarball-url>
```

npm parses the server's JSON response body but replaces it with generic login guidance for E401. Use an anonymous GET to see Azure Artifacts' actual explanation:

```powershell
curl.exe --silent --show-error --write-out "`nHTTP %{http_code}`n" <tarball-url>
```

Do not use `curl --head`; Azure npm tarball endpoints can return 405 for HEAD while GET works.

### 4. Distinguish saved and upstream-only versions

An anonymous tarball GET has these useful outcomes:

- `303`: the version is saved locally and redirects to its blob.
- `401` with `No local versions ... access versions from upstream that have not yet been saved`: the version is visible upstream but not saved locally.

Confirm using the Azure Artifacts packaging API with `includeAllVersions=true`. A saved version has a local `storageId`, a publish/save timestamp, and appears in the `@Local` view.

### 5. Avoid destroying the reproduction

An authenticated request for an upstream-only version mutates the shared feed by importing that version. Before making one:

1. capture the anonymous response;
2. capture the saved-version API state;
3. capture or schedule any remote reproduction needed to verify diagnostics;
4. tell the user that authentication will globally unblock anonymous clients.

Do not perform an authenticated probe merely to diagnose the problem.

### 6. Remediate

Prefer importing the intended package version over downgrading it:

```powershell
Set-Location <directory-containing-package.json-and-.npmrc>
& "$((git rev-parse --show-toplevel))\scripts\UpdateNpmDependencies.ps1"
```

This script authenticates and runs npm with `--prefer-online`, allowing Azure Artifacts to save missing upstream packages. Review `.npmrc` and lockfile changes afterward; never commit credentials.

Verify anonymous access after import:

```powershell
curl.exe --silent --show-error --location --output NUL `
  --write-out "HTTP %{http_code}; downloaded %{size_download} bytes`n" `
  <tarball-url>
```

Also run the exact anonymous `npm ci` that previously failed.

## Related Build Correctness

Feed remediation and failure propagation are separate:

- Importing the package fixes the immediate restore.
- Native npm/npx exit codes must still terminate `build.ps1`; otherwise CI can remain false green or package stale dependencies.

When changing diagnostics, preserve the original exit code, bound any extra network request, and only replay anonymous requests to the known public feed.
