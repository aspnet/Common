# Stage 1 - Prepare Internal Branch

This stage is for the **monthly release**. Servicing releases should follow [stages-1-2-servicing-branch.md](stages-1-2-servicing-branch.md) instead.

When preparing a public branch for internal release, apply the following changes:

## 1. Directory.Build.props

Add NU1507 warning suppression after the `TestNetCoreTargetFrameworks` PropertyGroup. Internal branches don't use package source mapping due to internal feeds:

```xml
<!-- Internal branches don't use package source mapping feature due to internal feeds, so disable NU1507 warning saying it should be used. -->
<PropertyGroup>
  <NoWarn>$(NoWarn);NU1507</NoWarn>
</PropertyGroup>
```

Insert this new PropertyGroup right after the closing `</PropertyGroup>` that contains `TestNetCoreTargetFrameworks`.

## 2. NuGet.config

Remove the entire `<packageSourceMapping>` section. This section looks like:

```xml
<!-- Define mappings by adding package patterns beneath the target source.
     https://aka.ms/nuget-package-source-mapping  -->
<packageSourceMapping>
  <packageSource key="dotnet-public">
    <package pattern="*" />
  </packageSource>
  <packageSource key="dotnet-eng">
    <package pattern="*" />
  </packageSource>
  <!-- ... more packageSource entries ... -->
</packageSourceMapping>
```

**Important**: Do NOT add new internal feed sources to NuGet.config - those are managed by Dependency Flow automation and will be added automatically.

## 3. eng/Versions.props

Update these two properties (do NOT change any version numbers):

Change `StabilizePackageVersion` from `false` to `true`:
```xml
<StabilizePackageVersion Condition="'$(StabilizePackageVersion)' == ''">true</StabilizePackageVersion>
```

Change `DotNetFinalVersionKind` from empty to `release`:
```xml
<DotNetFinalVersionKind>release</DotNetFinalVersionKind>
```

## 4. Locate the actual restore/build pipeline template

Inspect the selected baseline's pipeline call chain before editing. Authentication may live in `eng/pipelines/templates/RestoreAndBuild.yml`, not `BuildAndTest.yml`; apply setup at the actual restore boundary, not a stale filename/Node task anchor. Capture the complete internal-only file/hunk list and baseline blobs for Stage 6 cleanup, including every auth/private-feed change.

### Add Private Feeds Credentials Setup

Before authenticated restore, use the repository's existing credential setup pattern:

```yaml
  - task: PowerShell@2
    displayName: Setup Private Feeds Credentials
    condition: eq(variables['Agent.OS'], 'Windows_NT')
    inputs:
      filePath: $(Build.SourcesDirectory)/eng/common/SetupNugetSources.ps1
      arguments: -ConfigFile $(Build.SourcesDirectory)/NuGet.config -Password $Env:Token
    env:
      Token: $(<private-feed-read-token>)

  - task: Bash@3
    displayName: Setup Private Feeds Credentials
    condition: ne(variables['Agent.OS'], 'Windows_NT')
    inputs:
      filePath: $(Build.SourcesDirectory)/eng/common/SetupNugetSources.sh
      arguments: $(Build.SourcesDirectory)/NuGet.config $Token
    env:
      Token: $(<private-feed-read-token>)
```

### Comment Out Integration Tests

Comment out the integration tests step as they require authentication to private feeds that isn't available during internal release builds:

```yaml
  - ${{ if ne(parameters.skipTests, 'true') }}:
    # Skipping integration tests for now as they require authentication to the private feeds
    # - script: ${{ parameters.buildScript }}
    #           -integrationTest
    #           -configuration ${{ parameters.buildConfig }}
    #           -warnAsError 1
    #           /bl:${{ parameters.repoLogPath }}/integration_tests.binlog
    #           $(_OfficialBuildIdArgs)
    #   displayName: Run integration tests
```

## 5. azure-pipelines.yml

Remove the `codecoverage` stage entirely. This is the stage that:
- Has `displayName: CodeCoverage`
- Downloads code coverage reports from build jobs
- Merges and validates combined test coverage
- Contains a `CodeCoverageReport` job

Also remove the `codecoverage` dependency from the post-build validation's `validateDependsOn` list:

```yaml
# Remove this conditional dependency block:
- ${{ if eq(parameters.runTests, true) }}:
  - codecoverage
```

## Files NOT to modify

- **eng/Version.Details.xml**: Version updates are managed by Dependency Flow automation
- **eng/Versions.props version numbers in Stage 1a**: product numbers are manual in Stage 1b; dependency versions are updated by darc in Stage 2
- **NuGet.config feed sources**: Internal darc feeds are added automatically by Dependency Flow

## Summary

| File | Action |
|------|--------|
| Directory.Build.props | Add `NU1507` to `NoWarn` in new PropertyGroup |
| NuGet.config | Remove entire `<packageSourceMapping>` section |
| eng/Versions.props | Set `StabilizePackageVersion=true`, `DotNetFinalVersionKind=release` |
| Actual restore/build/test template(s) | Add credentials before restore, comment out integration tests; record every internal-only hunk |
| azure-pipelines.yml | Remove `codecoverage` stage and its post-build dependency |
