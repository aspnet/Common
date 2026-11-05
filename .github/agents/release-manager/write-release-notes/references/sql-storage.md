# SQL Schema and Patterns

Use the SQL tool for all structured data storage during the release notes pipeline. Do **not** write intermediate files to disk.

## Core tables

```sql
CREATE TABLE prs (
    number INTEGER PRIMARY KEY,
    title TEXT,
    author TEXT,
    author_association TEXT,
    labels TEXT,             -- comma-separated label names
    merged_at TEXT,
    body TEXT,
    reactions INTEGER DEFAULT 0,
    is_candidate INTEGER DEFAULT 0,
    category TEXT,           -- 'changed', 'docs', 'tests', 'infra'
    packages TEXT            -- comma-separated package names from file paths
);

CREATE TABLE pr_packages (
    pr_number INTEGER NOT NULL,
    package_name TEXT NOT NULL,
    area_group TEXT NOT NULL,
    PRIMARY KEY (pr_number, package_name)
);

CREATE TABLE issues (
    number INTEGER PRIMARY KEY,
    title TEXT,
    body TEXT,
    labels TEXT,
    reactions INTEGER DEFAULT 0,
    pr_number INTEGER        -- the PR that references this issue
);

CREATE TABLE experimental_changes (
    pr_number INTEGER NOT NULL,
    package_name TEXT NOT NULL,
    change_type TEXT NOT NULL,  -- 'graduated', 'removed', 'breaking', 'added'
    diagnostic_id TEXT,         -- e.g. 'EXTEXP0001', 'MEAI001'
    description TEXT,
    PRIMARY KEY (pr_number, package_name, change_type)
);

CREATE TABLE pr_coauthors (
    pr_number INTEGER NOT NULL,
    coauthor TEXT NOT NULL,
    PRIMARY KEY (pr_number, coauthor)
);

CREATE TABLE pr_reviewers (
    pr_number INTEGER NOT NULL,
    reviewer TEXT NOT NULL,
    PRIMARY KEY (pr_number, reviewer)
);
```

## Common queries

### Find candidate PRs

```sql
SELECT * FROM prs WHERE is_candidate = 1 ORDER BY merged_at;
```

### PRs by area group

```sql
SELECT DISTINCT p.number, p.title, p.author, p.merged_at, pp.area_group
FROM prs p
JOIN pr_packages pp ON p.number = pp.pr_number
WHERE p.is_candidate = 1
ORDER BY pp.area_group, p.merged_at;
```

### Affected packages (for patch release scope)

```sql
SELECT DISTINCT package_name, area_group
FROM pr_packages pp
JOIN prs p ON pp.pr_number = p.number
WHERE p.is_candidate = 1
ORDER BY area_group, package_name;
```

### Popularity ranking

```sql
SELECT p.number, p.title, p.reactions,
       COALESCE(SUM(i.reactions), 0) AS issue_reactions,
       p.reactions + COALESCE(SUM(i.reactions), 0) AS total_reactions
FROM prs p
LEFT JOIN issues i ON i.pr_number = p.number
WHERE p.is_candidate = 1
GROUP BY p.number
ORDER BY total_reactions DESC;
```

### Experimental feature changes

```sql
SELECT ec.change_type, ec.package_name, ec.diagnostic_id, ec.description,
       p.number, p.title, p.author
FROM experimental_changes ec
JOIN prs p ON ec.pr_number = p.number
ORDER BY ec.change_type, ec.package_name;
```

### Category breakdown

```sql
SELECT category, COUNT(*) as count
FROM prs
WHERE is_candidate = 1
GROUP BY category;
```

### All contributors for acknowledgements

```sql
SELECT contributor, MIN(pr_number) as first_pr FROM (
    SELECT author AS contributor, number AS pr_number
    FROM prs
    WHERE is_candidate = 1

    UNION

    SELECT c.coauthor AS contributor, c.pr_number
    FROM pr_coauthors c
    JOIN prs p ON p.number = c.pr_number
    WHERE p.is_candidate = 1
)
WHERE contributor NOT LIKE '%[bot]%'
  AND contributor != 'Copilot'
GROUP BY contributor
ORDER BY first_pr;
```

### PR reviewers for acknowledgements

```sql
SELECT reviewer, COUNT(DISTINCT pr_number) as review_count
FROM pr_reviewers r
WHERE reviewer NOT LIKE '%[bot]%'
  AND reviewer != 'Copilot'
  AND reviewer NOT IN (SELECT DISTINCT author FROM prs WHERE is_candidate = 1)
  AND reviewer NOT IN (
      SELECT DISTINCT c.coauthor
      FROM pr_coauthors c
      JOIN prs p ON p.number = c.pr_number
      WHERE p.is_candidate = 1
  )
GROUP BY reviewer
ORDER BY review_count DESC;
```

## Usage notes

- Insert PRs as they are discovered during collection. Update `body`, `reactions`, and `packages` during enrichment.
- Insert into `pr_packages` after file path analysis determines affected packages (see [package-areas.md](package-areas.md)).
- Insert into `pr_coauthors` during enrichment when harvesting `Co-authored-by:` trailers from PR commits.
- Insert into `pr_reviewers` during enrichment when fetching PR reviews.
- Mark candidates with `is_candidate = 1` after filtering.
- Insert `experimental_changes` during the experimental feature audit step.
- Additional PRs can be added to the candidate list manually by number.
