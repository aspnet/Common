# .NET Extensions Repository

**Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.**

The .NET Extensions repository contains a suite of libraries providing facilities commonly needed when creating production-ready applications. Major areas include AI abstractions, compliance mechanisms, diagnostics, contextual options, resilience (Polly), telemetry, AspNetCore extensions, static analysis, and testing utilities.

## Working Effectively

### Prerequisites and Bootstrap
- **CRITICAL**: Ensure you have access to the appropriate Azure DevOps feeds. If build fails with "Name or service not known" for `pkgs.dev.azure.com`, this indicates network/authentication issues with required internal feeds.
- Install the .NET SDK version pinned in `global.json` (11.0.100-rc.3.26522.148) if you do not already have it:
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 11.0.100-rc.3.26522.148 --install-dir ~/.dotnet
  export PATH="$HOME/.dotnet:$PATH"
  ```
- Verify installation: `dotnet --version` should show `11.0.100-rc.3.26522.148`

### Essential Build Commands
- **Restore dependencies**: `./restore.sh` (Linux/Mac) or `restore.cmd` (Windows)
  - **CRITICAL - NEVER CANCEL**: Takes 5-10 minutes on first run. Always set timeout to 30+ minutes.
  - Downloads .NET SDK, tools, and packages from Azure DevOps feeds
  - Equivalent to `./build.sh --restore`

- **Build the entire solution**: `./build.sh` (Linux/Mac) or `build.cmd` (Windows)  
  - **CRITICAL - NEVER CANCEL**: Takes 45-60 minutes for full build. Always set timeout to 90+ minutes.
  - Repository has 124 total projects - build times are expected to be substantial
  - Without parameters: equivalent to `./build.sh --restore --build`
  - Individual actions: `--restore`, `--build`, `--test`, `--pack`

- **Run tests**: `./build.sh --test`
  - **CRITICAL - NEVER CANCEL**: Takes 20-30 minutes. Always set timeout to 60+ minutes.
  - Runs all unit tests across the solution

### Solution Generation (Key Workflow)
This repository does NOT contain a single solution file. Instead, use the slngen tool to generate filtered solutions:

- **Generate filtered solution**: `./build.sh --vs <keywords>`
  - Examples: 
    - `./build.sh --vs Http,Fakes,AspNetCore`
    - `./build.sh --vs Polly` (for resilience libraries)
    - `./build.sh --vs AI` (for AI-related projects)
    - `./build.sh --vs '*'` (all projects - escape asterisk)
  - Creates `SDK.sln` in repository root
  - Also performs restore operation
  - **NEVER CANCEL**: Takes 5-15 minutes depending on filter scope. Set timeout to 30+ minutes.

- **Open in Visual Studio Code**: `./start-code.sh` (Linux/Mac) or `start-code.cmd` (Windows)
  - Sets up environment variables for proper .NET SDK resolution
  - Opens repository in VS Code with correct configuration

### Build Validation and CI Requirements
- **Always run before committing**:
  ```bash
  ./build.sh --restore --build --test
  ```
- **Check for API changes**: If you modify public APIs, run `./scripts/MakeApiBaselines.ps1` to update API baseline manifest files
- **NEVER CANCEL** long-running builds or tests - this repository has hundreds of projects and build times are expected to be lengthy

### Common Build Issues and Workarounds

1. **"Workload manifest microsoft.net.sdk.aspire not installed"**:
   - Run `git clean -xdf` then restore again
   - Caused by multiple SDK installations

2. **"Could not resolve SDK Microsoft.DotNet.Arcade.Sdk"** or feed access errors:
   - Indicates no access to internal Microsoft Azure DevOps feeds
   - **NOT A CODE ISSUE** - environment/network limitation
   - Document as "Build requires access to internal Microsoft feeds"

3. **SSL connection errors during restore**:
   - Try disabling IPv6 on network adapter
   - May indicate network/firewall restrictions

### Project Structure and Navigation

#### Key Directories
- `src/` - Main source code:
  - `src/Analyzers/` - Code analyzers
  - `src/Generators/` - Source generators  
  - `src/Libraries/` - Core extension libraries
  - `src/Packages/` - NuGet package definitions
  - `src/ProjectTemplates/` - Project templates
- `test/` - Test projects (organized to match src/ structure)
- `docs/` - Documentation including `building.md`
- `scripts/` - PowerShell automation scripts
- `eng/` - Build engineering and configuration

#### Build Outputs  
- `artifacts/bin/` - Compiled binaries
- `artifacts/log/` - Build logs (including `Build.binlog` for MSBuild Structured Log Viewer)
- `artifacts/packages/` - Generated NuGet packages

#### Key Files
- `global.json` - Specifies the required .NET SDK version and runtime bands
- `Directory.Build.props` - MSBuild properties for entire repository
- `NuGet.config` - Package source configuration (internal Microsoft feeds)

## Validation Scenarios

**After making changes, always execute these validation steps**:
1. **Generate relevant filtered solution**: `./build.sh --vs <relevant-keywords>`
   - For AI libraries: `./build.sh --vs AI`
   - For AspNetCore: `./build.sh --vs AspNetCore`
   - For telemetry: `./build.sh --vs Telemetry,Logging,Metrics`
   - For resilience: `./build.sh --vs Polly,Resilience`
2. **Build and test**: `./build.sh --build --test` (remember: NEVER CANCEL, 60+ minute timeout)
3. **For public API changes**: Run `./scripts/MakeApiBaselines.ps1` to update API baseline manifest files
4. **Manual verification**: 
   - Check that your changes compile across target frameworks (net8.0, net9.0, net10.0, net11.0)
   - Review generated packages in `artifacts/packages/` if applicable
   - Verify no new build warnings in `artifacts/log/Build.binlog`

**Cannot run applications interactively** - this is a library repository. Validation is primarily through unit tests and integration tests.

**Common validation patterns by library type**:
- **Source generators** (Microsoft.Gen.*): Build consumer projects that use the generator
- **AspNetCore extensions**: Build test web applications that reference the extensions  
- **Testing utilities**: Use them in test projects to verify functionality
- **Analyzers**: Build projects that trigger the analyzer rules

## Time Expectations and Timeouts

**CRITICAL - NEVER CANCEL BUILD OR TEST COMMANDS**:
- **First-time setup**: 15-20 minutes (SDK download + initial restore) - timeout: 45+ minutes
- **Restore operation**: 5-10 minutes - **ALWAYS set timeout to 30+ minutes, NEVER CANCEL**  
- **Full build**: 45-60 minutes - **ALWAYS set timeout to 90+ minutes, NEVER CANCEL**
- **Test execution**: 20-30 minutes - **ALWAYS set timeout to 60+ minutes, NEVER CANCEL**
- **Filtered solution generation**: 5-15 minutes - **ALWAYS set timeout to 30+ minutes**

**Repository has 124 total projects** - build times are substantial by design. If commands appear to hang, wait at least 60 minutes before considering alternatives.

## Advanced Usage

### Custom Solution Generation
```bash
# Using PowerShell script directly with options
./scripts/Slngen.ps1 -Keywords "Http","Fakes" -Folders -OutSln "MyCustom.sln"
```

### Build Configuration Options
- `--configuration Debug|Release` - Build configuration
- `--verbosity minimal|normal|detailed|diagnostic` - MSBuild verbosity
- `--onlyTfms "net8.0;net9.0;net10.0;net11.0"` - Build specific target frameworks only

### Code Coverage
```bash
./build.sh --restore --build --configuration Release --testCoverage
```
Results available at: `artifacts/TestResults/Release/CoverageResultsHtml/index.html`

## Common Tasks Reference

**Key library areas and their keywords for filtered solutions**:
- **AI libraries**: `./build.sh --vs AI` (Microsoft.Extensions.AI.*, embedding, chat completion)
- **Telemetry**: `./build.sh --vs Telemetry,Logging,Metrics` (logging, metrics, tracing)
- **AspNetCore**: `./build.sh --vs AspNetCore` (middleware, diagnostics, testing)
- **Resilience**: `./build.sh --vs Polly,Resilience` (Polly integration, resilience patterns)
- **Compliance**: `./build.sh --vs Compliance,Audit` (data classification, audit reports)
- **Hosting**: `./build.sh --vs Hosting,Options` (contextual options, ambient metadata)
- **Testing utilities**: `./build.sh --vs Testing,Fakes` (mocking, fake implementations)

**Find projects by pattern**:
```bash
# AI-related projects
find src/Libraries -name "*AI*" -name "*.csproj"

# All AspNetCore extensions  
find src/Libraries -name "*AspNetCore*" -name "*.csproj"

# Source generators
find src/Generators -name "*.csproj"

# Test projects for a specific library
find test/Libraries -name "*[LibraryName]*" -name "*.csproj"
```

**Working with specific libraries workflow**:
1. Identify library area: `ls src/Libraries/` or use find commands above
2. Generate focused solution: `./build.sh --vs <library-keywords>`
3. Navigate to library directory: `cd src/Libraries/Microsoft.Extensions.[Area]`
4. Check corresponding tests: `cd test/Libraries/Microsoft.Extensions.[Area].Tests`
5. Review library README: `cat src/Libraries/Microsoft.Extensions.[Area]/README.md`
6. Build and test: `./build.sh --build --test` (with appropriate timeouts)