# Collect and Filter PRs

Gather all merged PRs between the previous release tag and the target for this release.

## Determine the commit range

1. **Previous release tag**: Use `gh release list --repo dotnet/extensions --exclude-drafts --limit 10` to find the most recent published release. If the user specifies a particular previous version, use that instead.
2. **Target**: Resolve the official build source SHA and verify it against every selected nuspec repository commit. Never default to `main`/HEAD or a reconciliation merge; a branch/tag input must resolve to that same source identity.
3. Verify both refs exist: `git rev-parse <previous-tag>` and `git rev-parse <target>`.

## Servicing override for backports

For servicing releases, treat the merged servicing-prep PR as canonical scope input.

1. Collect the `release/*` PRs listed or implied by that servicing-prep scope.
2. For each `release/*` backport PR, resolve its source `main` PR from title/body references
   (for example `Backport of #NNNN` or trailing `(#NNNN)`).
3. Use the source `main` PR as the release-notes entry and attribution source, even when the source
   PR merge date falls outside the tag-to-tag range.
4. Keep the `release/*` wrapper PR excluded unless it introduces distinct release-only user-facing
   behavior not present in the source PR.

## Search for merged PRs

### Primary — GitHub MCP server

Use `search_pull_requests` to find PRs merged in the date range. Keep result sets small to avoid large responses being saved to temp files.

```
search_pull_requests(
  owner: "dotnet",
  repo: "extensions",
  query: "is:merged merged:<start-date>..<end-date>",
  perPage: 30
)
```

Page through results (incrementing `page`) until all PRs are collected. The `start-date` is the merge date of the previous release tag's PR (or the tag's commit date); the `end-date` is the target commit date.

If the date range yields many results, split by week or use label-scoped queries to keep individual searches small.

### Fallback — GitHub CLI

If the MCP server is unavailable:

```bash
gh pr list --repo dotnet/extensions --state merged \
  --search "merged:<start-date>..<end-date>" \
  --limit 500 --json number,title,labels,author,mergedAt,url
```

## Assign packages from file paths

For each PR, fetch the list of changed files:

```
pull_request_read(
  method: "get_files",
  owner: "dotnet",
  repo: "extensions",
  pullNumber: <number>
)
```

Extract package names from file paths matching `src/Libraries/{PackageName}/`. For each matched package, look up the area group from [package-areas.md](package-areas.md).

**Rules:**
- A PR may affect multiple packages across different areas — record all of them in the `pr_packages` table
- If a PR only touches `test/Libraries/{PackageName}/`, it still maps to that package's area (useful for the "Test Improvements" category)
- If a PR only touches `eng/`, `scripts/`, `.github/`, or root-level files, it has no package assignment — categorize as infrastructure
- If a PR only touches `docs/`, it has no package assignment — categorize as documentation

**Fallback for ambiguous PRs:**
If a PR has no `src/Libraries/` or `test/Libraries/` file changes but does have `area-*` labels, use those labels to infer the package area. Map `area-Microsoft.Extensions.AI` to the AI area group, etc.

## Store PR data

Insert each discovered PR into the `prs` SQL table. See [sql-storage.md](sql-storage.md) for the schema.

## Enrich PR details

For each PR, fetch the full body and metadata:

```
pull_request_read(
  method: "get",
  owner: "dotnet",
  repo: "extensions",
  pullNumber: <number>
)
```

Update the `body`, `reactions`, `author_association`, and `labels` columns. Multiple independent PR reads can be issued in parallel.

Also fetch comments to look for Copilot-generated summaries:

```
pull_request_read(
  method: "get_comments",
  owner: "dotnet",
  repo: "extensions",
  pullNumber: <number>
)
```

Also fetch reviews for the acknowledgements section:

```
pull_request_read(
  method: "get_reviews",
  owner: "dotnet",
  repo: "extensions",
  pullNumber: <number>
)
```

Record each reviewer's username and the PR number. See [editorial-rules.md](editorial-rules.md) for exclusion and sorting rules.

## Discover linked issues

Parse the PR body for issue references:
- Closing keywords: `Fixes #1234`, `Closes #1234`, `Resolves #1234`
- Full URL links: `https://github.com/dotnet/extensions/issues/1234`
- Cross-repo references: `dotnet/extensions#1234`

For each discovered issue, fetch details with `issue_read` and insert into the `issues` table.

## Deduplicate against prior release

PRs merged into `main` may include changes that were already included in a prior release via a `release/` branch. The prior release branch may also contain PRs that were merged into it but never covered in that release's notes — those PRs must still be excluded from the current release notes because they shipped in the prior release's packages.

### Fetch release branches

Before deduplication, ensure the relevant release branches are available locally:

1. Identify which local git remote points to `dotnet/extensions` on GitHub (e.g. by checking `git remote -v` for a URL containing `dotnet/extensions`). Use that remote name in subsequent fetch commands.
2. Identify the prior release branch from the previous release tag (e.g. `v10.3.0` → `release/10.3`).
3. Fetch it: `git fetch <remote> release/10.3` (using the remote identified in step 1).
4. If the current release also has a release branch (e.g. `release/10.4`), fetch that too.

### Exclude PRs already shipped

For each candidate PR, check whether it was already included in any prior release — even if the prior release notes didn't mention it:

1. **Check against the prior release tag**: `git merge-base --is-ancestor <pr-merge-commit> <previous-tag>`. If the PR's merge commit is an ancestor of the previous release tag, it shipped in that release — exclude it.
2. **Check shipped servicing tags/build sources**, not an advancing release branch HEAD. A commit on a branch after its last shipped tag did not necessarily ship. Check ancestry against each relevant published servicing source and compare patch equivalence (`git patch-id --stable`, or equivalent `git cherry` evidence) for cherry-picks with different SHAs. Limit matches to the package scope actually shipped by those builds.
3. **Check the prior release notes body**: Fetch the GitHub release for the previous tag and check if the PR number appears in the release notes body. This catches PRs that were explicitly covered.
4. **Harvest `main`-PR references from every PR merged into the prior release branch**: Backport PRs name the `main` PR number(s) they carry -- in the PR **title** (a trailing `(#NNNN)`) and/or **body** (`Backport of #NNNN`, or a checklist/list of numbers). This is the reliable signal, and it handles both shapes of backport:
   - **Single-PR backport**: `[release/10.7] Fix ToolJson.AdditionalProperties to accept sub-schema objects` (#7547) references its `main` twin #7546 in the body.
   - **Aggregate backport**: `Stage an MEAI 10.4.1 release` (#7402) references **thirteen** `main` PRs in its body (`#7285, #7340, #7369, #7373, #7379, #7381, #7382, #7387, #7389, #7394, #7398, #7399, #7400`) and has **no `[release/...]` title twin at all**. Title-matching would miss every one of them.

   Build a "shipped-via-backport" set from prior prep/backport PRs whose changes are verified in a **published** source. Extract actual source/backport PR references from title/body, not every issue/reference number indiscriminately; verify each against shipped graph/patch evidence. Use prior release notes to corroborate, not to assume unshipped branch work was released.
5. **Fallback -- title equivalence**: Normalize titles to discover potential pairs when references are missing, then verify shipped diff/patch equivalence. A title match alone is not exclusion evidence. Keep an uncertainty row if equivalence cannot be established.

> **Why this matters:** A change already shipped in the prior release can reappear in the next release's date-range search in two ways:
> - A PR merged directly into a `release/` branch ships in that release but may never appear in its notes; when the branch later merges into `main` it surfaces in the next release's search. The ancestry checks (1-2) catch this.
> - A change is **backported**: it merges into `main` as its own PR (new to the next release's lineage, so ancestry misses it) and separately ships in the prior release via a backport PR merged into that release branch. The backport PR names the `main` PR number(s) it carries -- **one** number for a single-PR backport, **many** for an aggregate backport -- so harvesting those references (4) is what excludes the `main` half. The notes-body number check (3) and title fallback (5) are backups for the rare backport that doesn't cite its source PR numbers.

Run all applicable evidence checks before marking candidates. Date searches only discover PRs: ground inclusion in the shipped commit graph/diffs and patch equivalence, and exclude already shipped servicing changes even when their source PR merge SHA is not an ancestor. Store the prior tag/source/package and exclusion evidence per PR.

## Exclusion filters

Before marking PRs as candidates, exclude:
- PRs labeled `backport`, `servicing`, or `NO-MERGE`
- PRs whose title starts with `[release/` or contains `backport`
- PRs that are purely automated version bumps (title matches `Update version to *` and only changes `Directory.Build.props` or version files)

Mark remaining PRs as candidates: `UPDATE prs SET is_candidate = 1 WHERE ...`

## Populate co-author data

For each candidate PR, collect co-authors from **all commits in the PR**, not just the merge commit:

1. **Fetch the PR's commits** via the pull request commits endpoint (for example, using a `pull_request_read` / PR-scoped `list_commits` method), so it works even if the PR's head branch has been deleted. If needed, also use `get_commit` for the merge commit SHA from the PR details.
2. **Parse `Co-authored-by:` trailers** from every commit message in the PR. These trailers follow the format: `Co-authored-by: Name <email>`. Extract the GitHub username from the email (e.g. `123456+username@users.noreply.github.com` → `username`) or match the name against known GitHub users.
3. **Also check the merge commit** message itself for `Co-authored-by:` trailers, as squash-merged PRs consolidate trailers there.
4. **Check the `copilot_work_started` timeline event** to identify Copilot-assisted PRs where a human delegated the work.

A common pattern in this repository is a human-authored PR with `Co-authored-by: Copilot <...>` trailers on individual commits — these must be detected to give Copilot co-author attribution. Store all discovered co-authors in the database for use during rendering.
