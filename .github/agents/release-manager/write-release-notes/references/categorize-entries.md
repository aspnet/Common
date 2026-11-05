# Categorize Entries

Sort candidate PRs into sections and group them by package area for the release notes.

## Step 1: Assign categories

For each candidate PR, assign one of these categories based on the primary intent:

| Category | Key | Content |
|----------|-----|---------|
| What's Changed | `changed` | Features, bug fixes, API improvements, performance, breaking changes |
| Documentation Updates | `docs` | PRs whose sole purpose is documentation |
| Test Improvements | `tests` | Adding, fixing, or improving tests |
| Repository Infrastructure Updates | `infra` | CI/CD, dependency bumps, version bumps, build system, agents, workflows and skills |

**Decision rules:**
- If a PR modifies files under `src/Libraries/` or `src/Generators/` or `src/Analyzers/`, it is `changed` (even if it also touches docs or tests)
- If a PR **only** modifies files under `docs/`, XML doc comments, or README files, it is `docs`
- If a PR **only** modifies files under `test/`, it is `tests`
- If a PR **only** modifies `eng/`, `scripts/`, `.github/`, CI YAML files, or root config files, it is `infra`
- When a PR spans multiple categories, assign based on primary intent — read the title and description

Update the SQL record:
```sql
UPDATE prs SET category = '<category>' WHERE number = <pr_number>;
```

## Step 2: Group by package area

For PRs in the `changed` category, group them under their package area headings using the `pr_packages` table. Each area heading uses the descriptive name from [package-areas.md](package-areas.md).

**Area heading selection:**
- If a PR affects packages in a single area → place under that area
- If a PR affects packages in multiple areas → place under the area most central to the change, noting the cross-cutting nature in the description if relevant
- If a `changed` PR has no package assignment (rare — e.g. a cross-cutting change to `Directory.Build.props` that affects all packages) → place under a "Cross-Cutting Changes" heading

**Area ordering in the release notes:**
Order areas by the number of entries (most active area first), then alphabetically for ties. This naturally highlights the areas with the most changes.

## Step 3: Impact tiering within areas

Within each area, order entries by impact:

1. **Breaking changes** (stable API breaks — should be very rare)
2. **Experimental API changes** (graduated, removed, breaking — see [experimental-features.md](experimental-features.md))
3. **New features and significant improvements**
4. **Bug fixes with community signal** (reported by community members, high reaction count)
5. **Other bug fixes and improvements**

Use the popularity score from the SQL `prs` + `issues` tables (combined reaction counts) as a tiebreaker within each tier.

## Step 4: Handle documentation, test, and infrastructure categories

These categories are **not** grouped by package area. They appear as flat lists in their own sections at the bottom of the release notes:

- **Documentation Updates** — sorted by merge date
- **Test Improvements** — sorted by merge date
- **Repository Infrastructure Updates** — sorted by merge date

## Full vs. patch release considerations

### Monthly release
- All areas with changes get their own heading
- All four category sections appear (omit empty ones)
- Include the "Experimental API Changes" section if any experimental changes were detected

### Targeted patch release
- Only the affected areas appear (typically 1–3 areas)
- The preamble explicitly states which packages are included in the patch
- The "Experimental API Changes" section still appears if relevant
- Documentation, test, and infrastructure sections may be shorter or absent

## Multi-faceted PRs

A single PR may deliver a feature, fix bugs, AND improve performance. Use the verbatim PR title as the entry description regardless. Read the full PR description, not just the title, to determine the correct category assignment.
