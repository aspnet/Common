# Adding a .NET target framework

Adding a TFM is a release change, not just a project-file edit. The .NET 10
transition (notably `8df3159143`) is a useful precedent; keep existing TFMs and
their dependency bands intact when adding the next one.

1. **SDK and feeds.** Pin an SDK that understands the new TFM in `global.json`,
   including the `tools.runtimes` entries needed by builds and tests. Check the
   Arcade/Helix SDK pins and build agents against it. Check exact dependency
   versions on the existing trusted feeds first, including `dotnet-public`;
   a new TFM does not require its own feed. Add a version-specific public Azure
   Artifacts feed and mapping only for packages unavailable on existing feeds.
   Check the isolated template execution sandbox's `projects/nuget.config`
   separately; it clears inherited sources and retains local shipping plus
   nuget.org for generated projects. Change its sources only when necessary
   and verify both framework and third-party dependencies resolve.
   Internal release branches have a separate, darc-managed feed flow; verify
   both `eng/common/SetupNugetSources` scripts recognize the new public feed
   and add its internal and internal-transport counterparts.
2. **TFMs and versions.** Advance `LatestTargetFramework` and the product
   `MajorVersion`/`MinorVersion` in `Directory.Build.props` and
   `eng/Versions.props`. Preserve the older `NetCoreTargetFrameworks`, optional
   `.targetframeworks` filtering, legacy TFMs, and stage-specific prerelease
   branding. Inspect any per-project version override before changing it.
   Check hard-coded `<TargetFrameworks>` in libraries and tests.
3. **Dependency bands.** Add `eng/packages/General-net<N>.props` and import it
   from `General.props`. Give each supported TFM its own package versions;
   update `General-LTS.props`, older bands, and `Tests.props` so no TFM silently
   falls back to another band's versions. `ForceLatestDotnetVersions` should
   select the latest *compatible* band for each TFM, not dependencies that
   require a newer framework. Verify *every* package
   exists at the chosen version on a trusted feed, including `System.*` and
   ASP.NET Core test dependencies. Check the internal API-docs transport
   package for transitive assembly-version conflicts. Keep the coherent,
   darc-managed primary band in `Version.Details.xml` separate from older/newer
   overlays in `Versions.props`; the internal release process uses BAR IDs and
   `darc update-dependencies` to advance them.
   Check the new framework's `PackageOverrides.txt` for packages now supplied by
   the framework. For example, .NET 11 includes `Microsoft.Extensions` abstraction
   assemblies such as `Microsoft.Extensions.Options`. Condition redundant
   `PackageReference` items with `!IsTargetFrameworkCompatible` for the new TFM,
   retaining them for older targets instead of suppressing NU1510. Check
   multi-target libraries even when legacy targets prevent NU1510 from being
   reported, as well as tests, shared code, and engineering tools.
4. **Source, tools, and templates.** Search for TFM-specific MSBuild conditions
   and `#if NET<N>_0[_OR_GREATER]` checks. Only change guards where a runtime API
   or behavior actually changes. Retire polyfills that now collide with framework
   APIs and keep explicit ordinal comparisons and cancellation forwarding when
   new overloads appear. Check compatibility suppressions and API
   baselines when the assembly surface changes. Update ApiChief paths and the
   `run-apichief` instructions. Add the TFM to template choices, defaults,
   generated projects, execution-test matrices, and generated snapshots.
5. **Validation.** Evaluate the actual target frameworks and central
   `PackageVersion` values for each band. Restore, build, test, and pack with the
   repository build scripts on the new SDK; exercise template integration
   tests and inspect produced `.nuspec` dependency groups and lib TFMs.
   Disable MSBuild node reuse when capturing generated-project build output,
   so orphaned nodes cannot hold the redirected streams open. Check
   the release-manager playbooks and CI for SDK, feed, and release-band
   assumptions before publishing.
