# Pull request description

The automation maintains a **single** continuously-updated draft PR against `main`:

- **Branch**: `update-agent-framework-template` (evergreen; if the remote branch already exists from a
  prior closed PR, suffix with the run id).
- **Labels**: `automation`, `area-ai-templates` (required for the automation to own and act on it).
- **Title**: `Update Agent Framework to <release_version>`.
- **Draft**: yes. The automation never marks ready or merges; a human reviews and merges.

## Body

Regenerate the entire body on every full-body write. Keep it factual and short:

1. One line: bumping the Microsoft.Agents.AI packages to Agent Framework `<release_version>`
   (released `<release_date>`), sourced from the `<source_feed>` feed.
2. A table of version changes -- one row per entry in `desired_versions`, `old -> new`.
3. The **template package version** change: `template_pkg_old -> template_pkg_new` (the
   `Microsoft.Agents.AI.ProjectTemplates` package's own version, aligned to the release
   Major/Minor/Patch with its prerelease label unchanged).
4. **CI validation**: state that `eng/packages/ProjectTemplates.props` and the template package
   version were bumped and the template package restored + built + packed successfully through the
   repo's Arcade build (quote `build_summary`), and that the snapshot + execution tests passed (quote
   `tests_summary`).
5. **Agent Framework changes reviewed**: note the `af_change_count` releases evaluated across
   `from_version -> release_version`, and either the consumption updates made or that none were
   required.
6. A note that the automation maintains this draft; a human reviews and merges.

## Tracking block (required, verbatim, as the very last thing in the body)

Wrap it in a `yaml` code fence -- the fence lines are **required** so the `#` marker lines render as
code instead of Markdown headings. Reproduce the fence and block exactly (opening ```` ```yaml ````,
the block, closing ```` ``` ````):

```yaml
# agent-framework-template:state:begin
source-feed: <source_feed>
agent-framework-version: <release_version>
agent-framework-release-date: <release_date>
feedback-processed-through: <run_started_at>
# agent-framework-template:state:end
```

The next run reads this block back to recover its place: `agent-framework-version` tells it which
release the PR is already at (caught-up vs behind), and `feedback-processed-through` is the watermark
below which review activity is considered already handled. The worker's post-run identity check fails
the run if a full-body PR write omits the markers or the `feedback-processed-through` watermark.
