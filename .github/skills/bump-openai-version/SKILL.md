---
name: bump-openai-version
description: Upgrade the OpenAI .NET SDK used by Microsoft.Extensions.AI.OpenAI and validate restore, build, unit tests, and configured OpenAI integration tests.
---

# Bump OpenAI Version

Use this skill when asked to upgrade the `OpenAI` NuGet package in this repository. Start with the assumption that the new SDK version is compatible: update the centrally managed version, restore, build, and test. Investigate upstream changes or adapt MEAI only when validation exposes a compatibility problem.

## Inputs

- Require the target OpenAI package version. If it is not supplied, ask for it rather than assuming that the latest version is wanted.
- Integration tests require credentials already configured as described in `test/Libraries/Microsoft.Extensions.AI.Integration.Tests/README.md`. Never request, print, copy, or commit credentials.

## Existing dotnet/extensions PR Preflight

Search open pull requests in `dotnet/extensions` before doing deeper analysis. Search for the target version plus terms such as `OpenAI`, `OpenAI SDK`, and `Microsoft.Extensions.AI.OpenAI`.

If a likely matching PR exists, report it and stop unless the user explicitly asks to continue. If GitHub access fails, report the failure and ask whether to proceed without the preflight.

## Interactive Workflow

### 1. Establish the baseline

1. Read `.github/copilot-instructions.md` in full and follow all applicable guidance, including its build, test, and timeout requirements.
2. Inspect `git status` and preserve unrelated worktree changes.
3. Read the centrally managed `OpenAI` version in `eng/packages/General.props` and verify it is not already the requested target. If it is already at the target version, report that and stop.
4. Run the focused OpenAI unit-test baseline in Debug configuration before changing code when the worktree and available feeds allow it. If the baseline has pre-existing failures, report them and ask the user whether to proceed with the bump.

### 2. Verify package-source availability

Check whether the exact target version is available from the repository's configured `dotnet-public` feed before editing `NuGet.config`. Use the feed's NuGet V3 `PackageBaseAddress` resource rather than `SearchQueryService`: Azure Artifacts search responses may omit package versions and produce false negatives.

1. Fetch the `dotnet-public` service index from the URL configured in `NuGet.config`.
2. Find the resource whose `@type` starts with `PackageBaseAddress`.
3. Construct the documented package-content URL: `<PackageBaseAddress>/openai/<lower-normalized-version>/openai.<lower-normalized-version>.nupkg`.
4. Send a `GET` range request for the first byte. A successful request proves that the exact package version is downloadable.

Use `curl` with the documented [NuGet V3 Package Content API](https://learn.microsoft.com/nuget/api/package-base-address-resource). This example writes only one byte; remove the temporary file afterward:

```sh
curl --fail --silent --show-error --location --range 0-0 \
  --output openai-package-probe.tmp \
  'https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/flat2/openai/2.13.0/openai.2.13.0.nupkg'
```

Exit code 0 means the exact version is available; a nonzero exit code means either it is absent or the request failed, with `curl` printing request errors. Replace both occurrences of `2.13.0` with the lowercased, normalized target version. The `flat2` URL above is the current `PackageBaseAddress` advertised by the configured service index. Re-read the service index instead of assuming that path if the URL stops working.

Do not use `curl --head` against the package-content URL: Azure Artifacts returns HTTP 405 because this endpoint supports `GET`, not `HEAD`. If direct metadata lookup fails for a network or authentication reason, perform a focused restore for the exact version and inspect which source resolves the package. Do not interpret an incomplete `SearchQueryService` response as proof that a version is unavailable.

- If the version is in `dotnet-public`, leave `NuGet.config` unchanged.
- If it is not in `dotnet-public` but is available on NuGet.org, report that distinction and ask the user whether to proceed using NuGet.org temporarily or wait for the package to flow.
- If the target is an alpha, inspect the successful scheduled runs of [`release.yml`](https://github.com/openai/openai-dotnet/actions/workflows/release.yml) and verify the exact version from the uploaded `.nupkg` artifact. Scheduled runs produce `<VersionPrefix>-alpha.<run_number>` packages and publish them to OpenAI's GitHub Packages feed. Ask the user whether to proceed with temporary alpha-package source configuration.

### 3. Review checkpoint

Before modifying files, present the findings for user review:

- current and target versions;
- package-source availability;
- expected validation.

Wait for the user to approve or revise the proposed work before implementing the bump.

### 4. Upgrade and validate compatibility

1. Change only the centrally managed `OpenAI` version in `eng/packages/General.props`.
2. Restore and build the focused OpenAI projects first so compiler and package-resolution failures are quick to diagnose.
3. If restore, compilation, or tests expose a compatibility problem:
   - inspect the relevant upstream release notes, commits, source, or issue;
   - report the failure and proposed adaptation before making additional source changes;
   - after user approval, fix the incompatibility using the new SDK's intended APIs;
   - add or update focused regression tests for the affected behavior.
4. Revisit an existing workaround only when validation implicates it or the required compatibility fix touches it. Remove it if the new SDK makes it obsolete; otherwise retain it.

Prior bumps show the expected kinds of adaptation:

- OpenAI 2.12.0 changed a hosted-file API and allowed removal of an older `openai-dotnet` workaround.
- OpenAI 2.13.0 required guarding streaming updates whose `choices` array could be empty; the fix included focused MEAI coverage rather than indexing the first choice unconditionally.

### Interpreting integration-test failures

Do not treat every live-service failure as a package regression. Capture the HTTP status and endpoint, then compare it with current service support:

- authentication, quota, deployment, and model-availability failures are environment findings;
- a reproducible request/response conversion failure is likely an SDK or MEAI compatibility issue;

### 5. Validate

Validate the changes according to `.github/copilot-instructions.md`.

## Completion Report

Report:

- old and new OpenAI versions;
- whether the target resolved from `dotnet-public` or required a temporary source;
- any compatibility failures and MEAI adaptations;
- any workarounds changed as part of a compatibility fix;
- exact restore, build, unit-test, and integration-test results;
- integration environments exercised (public OpenAI and/or Azure OpenAI);
- service-side expected failures, including status and endpoint;
- validation not run and the concrete reason;
- the final files changed.

Before concluding, verify the final diff is limited to the version bump, necessary compatibility/test changes, API baselines when applicable, and any still-required package-source configuration.
