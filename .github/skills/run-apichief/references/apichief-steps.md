# ApiChief Execution Steps

## 1. Determine the ApiChief Command

Ask the user which command to run if not specified. The available commands are:

| Command | Description | Extra arguments |
|---|---|---|
| `emit baseline` | Emit a JSON API baseline | `-o <file>` for output path |
| `emit summary` | Emit a human-readable API summary | `-o <file>`, `-x` to omit XML docs |
| `emit review` | Emit API review files | `-o <dir>`, `-n` to group by namespace |
| `emit delta` | Emit a delta against a previous baseline | `<baseline-path>`, `-o <file>` |
| `check breaking` | Fail if breaking changes exist vs. baseline | `<baseline-path>` |

Default to `emit baseline` if the user only asks to "run ApiChief" without specifying a command. This updates the API baseline files in the source tree.

## 2. Identify the Target Libraries

1. Interpret the user's input (which may be a shorthand, abbreviation, or glob pattern) and match it against folder names in `src/Libraries/`.
2. List the matched libraries and confirm before proceeding if the match is ambiguous.

## 3. Build ApiChief

```powershell
dotnet build eng/Tools/ApiChief/ApiChief.csproj --nologo --verbosity q
```

Wait for this to succeed before proceeding.

## 4. Build the Target Libraries

The libraries must already be built so that compiled DLLs exist. Build the matched libraries if needed:

```powershell
# Build all matched libraries using the repo's build infrastructure
# Use --vs to generate and build a filtered solution
build.cmd --vs <keyword>   # Windows
./build.sh --vs <keyword>  # Linux/macOS
```

Where `<keyword>` is derived from the library names (e.g. `AI` for MEAI libraries, `AspNetCore` for ASP.NET Core libraries). Use the most appropriate keyword(s) for the matched set.

Alternatively, build individual projects directly:

```powershell
dotnet build src/Libraries/<LibraryName>/<LibraryName>.csproj --nologo --verbosity q
```

## 5. Run ApiChief

For each matched library, run ApiChief against the compiled DLL. The repo multi-targets libraries across several TFMs (e.g. `net11.0`, `net10.0`, `net9.0`, `net8.0`, `net462`, `netstandard2.0`). Use the **highest available `net*` TFM** under `artifacts/bin/$name/Debug/` by default (e.g. prefer `net11.0` over `net10.0`). Do not use `netstandard*` or `net4*` targets unless specifically requested.

```powershell
$chiefDll = "artifacts/bin/ApiChief/Debug/net11.0/ApiChief.dll"

# For each matched library:
$name = "<LibraryName>"
# Find the highest net* TFM available (e.g. net11.0, net10.0, net9.0, net8.0)
$tfm = Get-ChildItem "artifacts/bin/$name/Debug" -Directory |
    Where-Object { $_.Name -match '^net\d+\.\d+$' } |
    Sort-Object { [version]($_.Name -replace '^net','') } -Descending |
    Select-Object -First 1 -ExpandProperty Name
$dll = "artifacts/bin/$name/Debug/$tfm/$name.dll"

dotnet $chiefDll $dll <command> [options]
```

### TFM Reporting and Override

Before running ApiChief, list the selected TFM for each library so the user can see what will be used:

```
  Microsoft.Extensions.AI → net11.0 (available: net11.0, net10.0, net9.0, net8.0, net462, netstandard2.0)
  Microsoft.Extensions.AI.Abstractions → net11.0 (available: net11.0, net10.0, net9.0, net8.0, netstandard2.0)
```

If the user specifies a TFM override (e.g. "use net9.0 for MEAI.Abstractions"), use that target instead of the auto-selected one. The user may also specify a blanket override for all libraries (e.g. "use net9.0").

### Output Conventions

- **`emit baseline`**: Output to `src/Libraries/$name/$name.json` (matches `MakeApiBaselines.ps1` convention).
- **`emit summary`**: Print to console unless the user requests file output.
- **`emit review`**: Creates an `API.$name/` folder by default.
- **`emit delta`** / **`check breaking`**: Require a baseline path argument.

## 6. Post-Baseline Review

After running `emit baseline`, perform the following cleanup and review steps on each generated baseline file.

### Preserve original file encoding and whitespace

Before reviewing instruction comments, ensure the new baseline matches the committed file's encoding and trailing whitespace:

1. **UTF-8 BOM**: Compare whether the committed file had a BOM (`0xEF 0xBB 0xBF`). If the new file adds or removes a BOM relative to the committed version, restore the original BOM state.
2. **Trailing newlines**: Compare whether the committed file ended with a trailing newline. If the new file adds or removes trailing newlines, restore the original ending.

Do **not** prompt the user for these fixes — apply them silently. After processing all files, list any files that had BOM or trailing newline cleanup applied (e.g. *"Restored original encoding/whitespace for: Microsoft.Extensions.AI.json"*).

### Discard version-only changes

After encoding/whitespace cleanup, check each file's `git diff`. If the **only** change in a file is the assembly version number in the `"Name"` field (e.g. `Version=10.3.0.0` → `Version=10.4.0.0`), revert the file using `git checkout` — this is not a meaningful API change.

Do **not** prompt the user — revert silently. In the final summary, list any files that were reverted (e.g. *"Reverted version-only change: Microsoft.Extensions.AI.Abstractions.json"*).

### Instruction comments

Review the diff of each generated baseline file for **instruction comments**. These are `//` comment lines in the *previous* version of the JSON file that describe manual edits to apply after regeneration.

### How to detect instruction comments

1. Run `git diff` on each updated baseline file.
2. Look for removed lines (prefixed with `-`) that start with `//` and contain instructional language (e.g. "After generating", "manually edit", "replace", "change", "This is needed until").
3. These comments describe edits that should be applied to the *newly generated* file.

### Check referenced GitHub issues

Instruction comments (both in baseline JSON files and in this skill's own reference files) may reference GitHub issues as the reason for a workaround (e.g. `// See: https://github.com/icsharpcode/ILSpy/issues/829`).

For each unique GitHub issue URL found:

1. Extract the `owner`, `repo`, and `issue_number` from the URL.
2. Use the GitHub MCP server's `get_issue` tool to check the issue's current status.
3. If the issue is **closed**:
   - **Emphasize** this when presenting the workaround group to the user (e.g. *"⚠️ The underlying issue icsharpcode/ILSpy#829 has been **closed**. This workaround may no longer be needed."*).
   - This helps the user decide whether to skip the workaround and investigate whether an updated dependency resolves the problem.
4. If the issue is **open**, mention it neutrally (e.g. *"The underlying issue icsharpcode/ILSpy#829 is still open."*).

### How to present them

1. **Group** the instruction comments by the action they describe (e.g. all "replace `scoped` with `params`" comments form one group).
2. **Summarize** each group: describe the change and list the affected members/lines.
3. **Prompt the user** with two choices per group:
   - **Apply**: Make the described edits to the new baseline file and re-add the instruction comments above the affected lines.
   - **Skip**: Leave the new baseline as-is (the instruction comments will be dropped).

### Example

If the previous baseline contained:

```json
// After generating the baseline, manually edit this file to have 'params' instead of 'scoped'
// This is needed until ICSharpCode.Decompiler adds params collection support
"Member": "abstract string Foo.GetCacheKey(params System.ReadOnlySpan<object?> values);"
```

And the new baseline has:

```json
"Member": "abstract string Foo.GetCacheKey(scoped System.ReadOnlySpan<object?> values);"
```

Then summarize: *"4 members need `scoped` replaced with `params` (ICSharpCode.Decompiler limitation). The underlying issue icsharpcode/ILSpy#829 is still open. Apply these edits and restore the instruction comments?"*

If the referenced issue were closed, instead emphasize: *"4 members need `scoped` replaced with `params`. ⚠️ The underlying issue icsharpcode/ILSpy#829 has been **closed** — this workaround may no longer be needed. Apply these edits anyway, or skip?"*

## 7. Report Results

- Show the list of libraries processed.
- For `check breaking`, report pass/fail per library.
- For output commands, show where files were written.
- If any library DLL was not found, report it and suggest building that library first.
