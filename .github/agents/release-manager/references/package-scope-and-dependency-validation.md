# Package Scope and Dependency Validation

Whenever package scope is determined:

1. Separate independently changed or human-selected packages (**Selected**) from packages included
   only because an in-scope package depends on them (**Dependency closure**).
2. Recursively include every direct/transitive dependency produced by `dotnet/extensions` that is
   required at the release version. Record the requiring parent and exact dependency requirement.
3. Present both groups separately and obtain explicit human approval. Recompute after every scope
   adjustment; a dependency-required package cannot be held back.

Monthly releases start with all releasable packages. Servicing releases start with selected roots.
Preparation may use project metadata provisionally, but the official `PackageArtifacts` `.nuspec`
files determine the final closure and published manifest.

Pin each selected artifact's exact ID/version (including preview/template-specific versions), account, path/hash, source SHA, AzDO/BAR IDs and nuspec repository commit. Monthly does **not** imply a uniform version: stable, preview and independently versioned templates may coexist. Require all selected nuspec repository commits to match the official build source SHA; investigate any mismatch. Exclude internal transport packages; a required internal dependency is a packaging blocker. Publish only individually approved manifest paths, never a wildcard directory.

Before publishing, verify every in-scope exact-version dependency resolves from staged artifacts or approved sources. After propagation, repeat the check for every published package from clean, consumer-visible sources, including packages without `lib` DLLs. Record the source and result. Unresolved dependencies block publication/sign-off unless the user explicitly accepts a scoped exception under [the workflow contract](workflow.md). Separate API transport failure from confirmed missing versions; browser exact-version listings can establish listing but not consumer restore. Record source/feed, clean cache status, per-package/TFM results and failures (including no-lib packages). Scope/source changes invalidate the manifest and dependent approvals/build evidence.
