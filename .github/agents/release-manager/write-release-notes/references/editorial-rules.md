# Editorial Rules

## Tone

- Remain **objective and factual** — describe what was introduced or changed without editorial judgment
  - ✅ `Introduces new APIs for text-to-speech`
  - ✅ `Added streaming metrics for time-to-first-chunk and time-per-output-chunk`
  - ❌ `Adds significant advancements in AI capabilities`
  - ❌ `Previously there was no way to measure streaming latency`
- Avoid superlatives and subjective qualifiers ("significant", "major improvements", "exciting"). Simply state what was added, changed, or fixed.
- When context about the prior state is needed, keep it brief — one clause, not a paragraph — then pivot to the new capability

## Conciseness

- **No code samples** in release notes. This repository ships many packages and the release notes should be scannable, not tutorial-length.
- Each entry is a **single bullet point** using the verbatim PR title.
- Link to the PR for details (via `#PR` auto-link).
- If a PR touches multiple concerns, the PR title should capture the primary change. Do not rewrite it.

## Entry format

Use this format (GitHub auto-links `#PR` and `@user` in release notes):

```
* Description #PR by @author
```

For PRs with co-authors (harvested from `Co-authored-by` commit trailers):
```
* Description #PR by @author (co-authored by @user1 @user2)
```

For Dependabot PRs, omit the author:
```
* Bump actions/checkout from 5.0.0 to 6.0.0 #1234
```

For Copilot-authored PRs, check the `copilot_work_started` timeline event to identify the triggering user. That person becomes the primary author; `@Copilot` becomes a co-author:
```
* Add trace-level logging for HTTP requests #1234 by @author (co-authored by @Copilot)
```

## Entry naming

- Use the **verbatim PR title** as the entry description. Do not rewrite, rephrase, or summarize PR titles.
- The PR title is the author's chosen description of the change and should be preserved exactly as written.

## Attribution rules

> **Critical: Every attribution must be derived from the stored PR data, never fabricated or assumed.** When writing each release note entry, read the `author` field from the `prs` SQL table and the co-author data collected during enrichment. Do not write an `@username` attribution without having that username in the database for that PR.

- **PR author**: The `user.login` from the PR details — read this from the `prs` table when rendering each entry
- **Co-authors**: Harvest from `Co-authored-by` trailers in **all commits** in the PR (not just the merge commit). Individual commits often carry `Co-authored-by: Copilot <...>` trailers that are not present in the merge commit message. Fetch the PR's commits and parse trailers from each one. For squash-merged PRs, check the squash commit message which consolidates trailers.
- **Copilot-authored PRs**: If the PR author is `Copilot`, `copilot-swe-agent[bot]`, or the PR body mentions "Created from Copilot CLI" / "copilot delegate":
  1. Check the `copilot_work_started` timeline event to identify the triggering user
  2. If found, the triggering user becomes the primary author and `@Copilot` becomes a co-author
  3. If the event is missing, inspect assignees/comments/commit trailers for explicit delegation evidence. Being an assignee or merger alone does not establish authorship/delegation.
  4. If unresolved, retain the verified author and flag attribution uncertainty for review; never invent a human delegator. Use only verified handles for co-authors, reviewers and reporters.
- **Bots to exclude**: `dependabot[bot]`, `dotnet-maestro[bot]`, `github-actions[bot]`, `copilot-swe-agent[bot]`, and any account ending with `[bot]`

## Sorting

Within the **What's Changed** area sections, sort entries by **impact** (see [categorize-entries.md](categorize-entries.md) for the impact tier ordering). Within all other sections (Documentation Updates, Test Improvements, Repository Infrastructure Updates), sort entries by **merge date** (chronological order, oldest first).

## Category definitions

### What's Changed
Feature work, bug fixes, API improvements, performance enhancements, and any other user-facing changes. This includes:
- New API surface area
- Bug fixes that affect runtime behavior
- Performance improvements
- Changes that span code + docs (categorize by primary intent)

### Documentation Updates
PRs whose **sole purpose** is documentation:
- Fixing typos in docs
- Updating XML doc comments (when not part of a functional change)
- README updates

A PR that changes code AND updates docs belongs in "What's Changed."

### Test Improvements
PRs focused on test quality or coverage:
- Adding new tests
- Fixing broken or flaky tests
- Test infrastructure improvements

### Repository Infrastructure Updates
PRs that maintain the development environment:
- Version bumps
- CI/CD workflow changes
- Dependency updates (Dependabot)
- Build system changes
- Copilot instructions, agent, and skill updates

PRs that touch test code should never be categorized as Infrastructure.

## Acknowledgements section

Include an acknowledgements section at the bottom of the release notes:

1. **New contributors** — people making their first contribution in this release
2. **Issue reporters** — community members whose reported issues were resolved in this release, citing the resolving PR
3. **PR reviewers** — single bullet listing all reviewers, sorted by review count (no count shown)

### Collecting PR reviewers

For each candidate PR, fetch the reviews:

```
pull_request_read(
  method: "get_reviews",
  owner: "dotnet",
  repo: "extensions",
  pullNumber: <number>
)
```

Collect all users who submitted a review (any state: APPROVED, CHANGES_REQUESTED, COMMENTED, DISMISSED). Multiple reviews on the same PR by the same user count as one review for that PR.

**Exclusions — do not list as reviewers:**
- Bot accounts: any account ending with `[bot]`, `Copilot`, `copilot-swe-agent[bot]`
- Users who are already listed as PR authors or co-authors elsewhere in the release notes (they are already acknowledged)
- The PR author themselves (self-reviews)

**Sorting:** Sort reviewers by the number of distinct PRs they reviewed (descending). Do not show the count — just the sorted order.

**Format:** A single bullet with all reviewers listed inline:
```
* @user1 @user2 @user3 reviewed pull requests
```

## Inclusion criteria

Include a feature/fix if:
- It gives users something new or something that works better
- It's a community-requested change (high reaction count on backing issue)
- It changes behavior users need to be aware of

Exclude:
- Internal refactoring with no user-facing change
- Test-only changes (these go in "Test Improvements")
- Build/infrastructure changes (these go in "Repository Infrastructure Updates")
