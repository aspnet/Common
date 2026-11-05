# Build and Test Commands

The skill needs to restore, build, and test from a freshly-generated AI-filtered solution. Use the form that matches your environment.

Always remove any stale `SDK.sln*` files first — they cause build errors when present alongside a newly-generated filtered solution.

## Linux / macOS (Copilot Coding Agent runs here)

```bash
rm -f SDK.sln*
./build.sh -vs AI
./build.sh -build -test
```

## Windows (local development)

```powershell
Remove-Item SDK.sln* -Force -ErrorAction SilentlyContinue
.\build.cmd -vs AI -nolaunch
.\build.cmd -build -test
```

## Faster iteration (any platform)

A full build + test takes 45-60+ minutes. For inner-loop iteration on a single test class, use:

```bash
dotnet test test/Libraries/Microsoft.Extensions.AI.Tests/ --filter "FullyQualifiedName~OpenTelemetryChatClientTests"
```

## After implementation

If the public API surface changed, regenerate the API baselines:

- Linux / macOS: `pwsh ./scripts/MakeApiBaselines.ps1`
- Windows: `.\scripts\MakeApiBaselines.ps1`

**Discard baseline updates for unrelated libraries** — only keep baselines for libraries that were changed as part of the convention update.
