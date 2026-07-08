# Public Documentation Review and Update Plan

**Status:** Active audit and update plan.

**Scope:** Public documentation built by DocFX: `README.md`, root `index.md`, `CHANGELOG.md`, root `toc.yml`, `docs/**/*.md`, and `docs/**/toc.yml`, excluding `docs/dev-plans/**`. API metadata is in scope only for navigation/build verification, not for hand-authored content cleanup.

**Date:** 2026-07-08

## Purpose

DataLinq 0.8 changed the real shape of the product. The public docs mostly kept up, but not evenly. The main risk now is not that the docs are empty; it is that different pages teach slightly different mental models.

The cleanup should make the public docs answer four questions clearly:

1. How do I install, configure, generate models, query, and save?
2. What LINQ/query shapes are actually supported?
3. Where are the runtime, cache, AOT, WebAssembly, and provider boundaries?
4. Which pages are user documentation, maintainer evidence, or roadmap?

The standard is blunt: public docs should describe shipped behavior. Roadmap and implementation history belong in `docs/dev-plans/**` unless they are explicitly labeled as release context.

## Review Method

This review used:

- `docfx.json` and `docs/toc.yml` to identify the published documentation surface.
- A heading inventory for all public Markdown files.
- Stale-term searches for old CLI names, branch wording, roadmap leakage, and removed implementation names.
- Focused reads of onboarding, query, cache, CLI, config, provider, platform, internals, support-matrix, and contributing pages.
- Spot checks against current 0.8 release notes and code-level command options.

DocFX currently builds public docs from:

- root `README.md`, `index.md`, `CHANGELOG.md`, `LICENSE.md`, and `toc.yml`
- `api/**.yml` and `api/index.md`
- `docs/**/*.md` and `docs/**/toc.yml`
- resources under `docfx/datalinq/public`, `public/**`, and `docs/schemas/*.json`

DocFX excludes `docs/dev-plans/**`, so dev-plan pages are source-only planning records.

## Findings

### P0: README Still Uses the Deprecated Model-Generation Command

`README.md` still tells users to run:

```bash
datalinq create-models -n AppDb
```

That command is now a deprecated compatibility alias. The active command surface is:

```bash
datalinq generate models -n AppDb
```

The README is the highest-traffic entry point, so this should be fixed first. While editing it, update the quickstart to start from `datalinq config init` and the public schema URL, matching the getting-started docs.

### P0: Query Execution Mental Model Is Now Too Simple

Several pages still teach "translate LINQ, select primary keys, hydrate through cache" as if it covers the whole LINQ execution story.

That remains correct for entity materialization and direct cache-aware reads, but 0.8 added paths that do not fit that diagram:

- SQL-backed scalar projections
- SQL-backed anonymous projection rows
- grouped aggregate rows
- SQL-backed joined projection rows
- derived-source pushdown after paging for grouped and joined row shapes

Affected pages:

- `docs/Querying.md`
- `docs/Caching and Mutation.md`
- `docs/internals/Architecture Overview.md`
- `docs/internals/Data Flow.md`
- `docs/internals/LINQ Parser Architecture.md`

The fix is not to delete the primary-key-first model. It is still useful. The fix is to split the query docs into:

- entity materialization path: primary-key-first, cache-aware
- SQL-backed row path: direct projection/grouped/joined aliases read from the data reader
- row-local projection path: materialize first, then compute in .NET

### P0: `LINQ Parser Architecture` Contains Stale Pre-Phase-21 Statements

`docs/internals/LINQ Parser Architecture.md` has newer 0.8 content, but also still contains older claims that now contradict the support matrix and `Query Translator` page.

Examples:

- It lists "filtering, ordering, paging, or terminal operators over explicit joined rows" as rejected, even though supported joined row composition and joined post-paging pushdown now exist for SQL-backed joined projection rows.
- It describes explicit join execution as selecting primary keys for both joined sources, buffering keys, hydrating through table caches, and evaluating the result selector over row objects. That is now only a fallback-style mental model for row-local joined projections, not the whole supported join path.
- Its parsing/rendering sections understate grouped aggregate projection, SQL-backed projection rows, implicit singular relation projection, query-syntax joins, and joined derived-source pushdown.

This page is maintainer-facing and influential. It should be updated before future query work starts, otherwise it will actively point contributors toward old implementation assumptions.

### P1: Public Docs Use Branch/Phase Wording Where Release Wording Would Be Clearer

Public pages still contain phrases such as:

- "current 0.8 branch"
- "current branch"
- "Phase 13"
- "Phase 21"
- local phase evidence paths

This wording is fine inside dev-plans. In public docs it makes the site feel like a worktree snapshot rather than product documentation.

Affected pages include:

- `docs/Supported LINQ Queries.md`
- `docs/support-matrices/LINQ Translation Support Matrix.md`
- `docs/internals/LINQ Parser Architecture.md`
- `docs/internals/Query Translator.md`
- `docs/Roadmap.md`
- `docs/Platform Compatibility.md`
- `docs/Benchmark Results.md`
- contributor tooling docs

The support matrix can keep phase history as maintainer evidence where useful, but normal user-facing pages should prefer release-neutral wording such as "in 0.8 and later" or "the current release".

### P1: `DataLinq.Dev.CLI` Docs Lag the Actual Release-Gate Options

`docs/contributing/DataLinq.Dev.CLI.md` is missing current command details:

- `size-report` exposes `--stop-on-publish-failure`, but the docs do not list it.
- `package-report` exposes `--allow-runtime-remotion`, but the docs do not list it.
- `size-report --fail-on-banned-payload` is described mainly as a Roslyn payload gate, but 0.8 release evidence also cares about Remotion runtime payloads.
- The `size-report` target is still named `phase8c` in code, but the docs should explain that this is a historical target-set name now used by the broader 0.8 compatibility gate.

This is contributor documentation, not end-user product documentation, but it is public and should match the CLI.

### P1: An Orphan Podman Investigation Page Is Built But Not Navigable

`docs/contributing/Podman MySQL MariaDB Environment Investigation.md` is included by DocFX but not linked from `docs/toc.yml`.

The content is useful, but it overlaps with:

- `docs/contributing/Dev and Test Environment.md`
- `docs/contributing/DataLinq.Testing.CLI.md`
- `docs/contributing/AI Assistant Guidance.md`
- `docs/support-matrices/Test Provider Matrix.md`

Recommended fix: fold the still-current port and Codex sandbox notes into the primary contributing/testing pages, then either delete the orphan page or move it under `docs/dev-plans/archive/` as historical investigation material. Leaving it public but hidden is the worst option.

### P1: There Is No Dedicated Schema Validation and Diff User Guide

Schema trust is a major product theme, but the public docs scatter it across:

- README quickstart bullets
- `docs/CLI Documentation.md`
- `docs/Configuration files.md`
- `docs/support-matrices/Provider Metadata Support Matrix.md`
- changelog entries

Users deserve a focused page under Usage, tentatively:

```text
docs/Schema Validation and Diff.md
```

That page should cover:

- `datalinq validate`
- `datalinq diff`
- exit codes
- text versus JSON output
- why `diff` is conservative and read-only
- what provider metadata support means
- what DataLinq still does not do: migrations, rename inference, destructive edits, applied-migration tracking

### P1: There Is No Practical Relations/Joins Guide

0.8 made relation/query behavior much more interesting, but the information is split across reference pages:

- lazy relation traversal in `Querying`
- cache behavior in `Caching and Mutation`
- relation predicates in `Supported LINQ Queries`
- implicit singular relation joins in `Supported LINQ Queries`
- explicit joins and query-syntax joins in `Supported LINQ Queries`
- internals in `Query Translator`

Add a practical page, tentatively:

```text
docs/Relations and Joins.md
```

It should explain the difference between:

- lazy generated relation property access
- one-to-many relation predicates translated as `EXISTS`
- singular relation member traversal translated as implicit inner joins
- explicit fluent `Join(...)`
- single query-syntax inner joins
- unsupported outer/multi/composite-key join shapes

This is the page a user should read before they assume relation traversal, joins, and projection all mean the same thing.

### P2: `Querying.md` Should Show More of the 0.8 Query Surface

`docs/Querying.md` currently gives a good conservative entry point but does not show the major 0.8-supported shapes in practical form.

Add short examples for:

- SQL-backed scalar and anonymous projections
- grouped aggregate projection
- explicit fluent join
- single query-syntax inner join
- singular relation member projection/filtering
- post-paging composition caveat

Keep `Supported LINQ Queries.md` as the complete contract. `Querying.md` should be the readable tour.

### P2: `model-generation.md` Is Too Thin for a Core Workflow Page

`docs/model-generation.md` is accurate but short. It should be expanded into a stronger reference for the supported generated-model edit surface.

Add or expand sections for:

- generated declaration files versus source-generator implementation files
- supported edits and unsupported edits
- partial classes for custom behavior
- enum preservation
- custom C# property types and future typed-ID/scalar-converter boundaries
- `--fresh`, `--overwrite-types`, and `--stamp-generated-header`
- how nullable context is chosen
- how generation failure avoids replacing output

Some of this already exists in `Configuration and Model Generation`; the cleanup should avoid duplication by making onboarding short and model-generation authoritative.

### P2: Platform Compatibility Is Accurate But Too Artifact-Centric for Normal Readers

`docs/Platform Compatibility.md` is honest, which is good. It also references local release evidence paths such as:

```text
artifacts/dev/compat-size-report/20260630-131026977/report.md
```

That is useful maintainer evidence, but a website reader cannot inspect the local artifact unless they have the release workspace. Keep the caveat, but consider restructuring the page:

- user-facing support claim first
- exact non-claims second
- maintainer verification commands and artifact paths later
- link to the changelog for the 0.8 evidence summary

Do not broaden the compatibility claim while making the page friendlier.

### P2: `Benchmark Results.md` Mixes Website Feature and Release Evidence

The benchmark page's published-trends section is user-facing. The 0.8 release-evidence artifact list is more maintainer/release-note material.

Options:

- keep a short "release evidence" note but move exact local artifact paths into dev-plans or release notes
- or add a "Maintainer Evidence" subsection that clearly explains those paths are repo-local artifacts, not website downloads

The page should continue saying the benchmark data is decision support, not marketing.

### P2: Roadmap Page Is Useful But Phase History Should Be Compressed

`docs/Roadmap.md` correctly says it describes direction, not shipped behavior. The issue is density: it still carries internal phase history in public.

Keep the roadmap public, but compress historical 0.8 phase detail and link to the changelog/dev-plan records instead. The public roadmap should prioritize:

- current shipped baseline
- 0.9 direction
- explicit non-claims
- deferred work

It should not be the place where readers learn the full phase numbering history.

### P3: Provider Pages Are Mostly Healthy But Need Better Cross-Links

`docs/backends/SQLite.md` and `docs/backends/MySQL-MariaDB.md` are good focused provider pages. The main improvement is cross-linking:

- link provider pages to `Schema Validation and Diff` once it exists
- link provider caveats to `Provider Metadata Support Matrix`
- link SQLite WebAssembly caveats to `Platform Compatibility`
- make the MySQL/MariaDB `Guid` caveat easier to discover from troubleshooting

### P3: Contributing Docs Are Good But Need Small Navigation Pruning

The contributor docs are much stronger than the old archive plan. Remaining cleanup:

- fold or remove the orphan Podman investigation page
- update `DataLinq.Dev.CLI` options
- avoid duplicating Codex-specific guidance across too many pages
- keep `AI Assistant Guidance` as the canonical agent page, not a place for general user docs

### P3: Do Not Remove These Pages

No current public page should be deleted outright except possibly the orphan Podman investigation page after its useful content is merged elsewhere.

Keep:

- `Supported LINQ Queries.md`
- support matrices
- internals pages
- platform compatibility
- roadmap
- benchmark results

Those pages have different audiences, but they are useful. The work is to sharpen boundaries and navigation, not to flatten the docs into one generic guide.

## Proposed Update Plan

### Phase 1: Correctness and Navigation Repair

Goal: remove immediately misleading public docs without starting the larger rewrite.

Tasks:

- Update README quickstart from `create-models` to `generate models`.
- Add `datalinq config init` to the README flow.
- Replace user-facing "current 0.8 branch" wording with release-neutral wording.
- Update `DataLinq.Dev.CLI` docs for `--stop-on-publish-failure`, `--allow-runtime-remotion`, and Remotion package-report gating.
- Decide the orphan Podman page outcome:
  - merge into primary contributing/testing docs and delete or archive, or
  - add it to the Contributing TOC with a less investigative title.
- Run `git diff --check`.

### Phase 2: Query Mental Model Realignment

Goal: make query docs internally consistent after 0.8.

Tasks:

- Update `Querying.md` to distinguish entity materialization, SQL-backed row projection, grouped aggregate rows, joined rows, and row-local projection.
- Update `Caching and Mutation.md` so the primary-key-first diagrams are scoped to entity reads, not all queries.
- Update `Architecture Overview.md` and `Data Flow.md` with the same split.
- Rewrite stale parts of `LINQ Parser Architecture.md`.
- Keep `Query Translator.md`, `Supported LINQ Queries.md`, and `LINQ Translation Support Matrix.md` synchronized.
- Run focused stale-text searches for old rejected join claims after editing.

### Phase 3: Add Missing User Guides

Goal: fill the largest conceptual holes instead of burying users in reference pages.

Tasks:

- Add `docs/Schema Validation and Diff.md`.
- Add `docs/Relations and Joins.md`.
- Add both pages to `docs/toc.yml` under Usage.
- Cross-link them from README, docs intro, CLI docs, Querying, provider pages, Troubleshooting, and support matrices where appropriate.

### Phase 4: Expand and Prune Reference Pages

Goal: make existing reference pages authoritative without duplicating onboarding.

Tasks:

- Expand `docs/model-generation.md`.
- Trim duplicate details from `Configuration and Model Generation.md` once `model-generation.md` is stronger.
- Restructure `Platform Compatibility.md` to separate reader-facing support claims from maintainer evidence paths.
- Decide whether exact benchmark release-evidence artifact paths stay in `Benchmark Results.md` or move to dev-plan/release-note context.
- Compress public roadmap phase history and link to changelog/dev-plans for full implementation history.
- Add provider-page cross-links to the new schema and relation docs.

### Phase 5: Verification

Goal: prove the documentation set builds and does not carry obvious stale references.

Commands:

```powershell
git diff --check
docfx docfx.json
rg -n "create-models|current 0\\.8 branch|filtering, ordering, paging, or terminal operators over explicit joined rows|For explicit joins, SQL selects primary keys" README.md index.md docs -g "*.md" -g "!docs/dev-plans/**"
```

Also run an explicit Markdown link-target validation script or equivalent check. `docfx` is necessary but not sufficient; deep relative links can still drift.

## Suggested Execution Order

The next work slice should be Phase 1 plus the minimum part of Phase 2 needed to remove contradictions. That gives a clean public baseline quickly.

After that, add the two missing user guides before doing broad prose polish. New pages will change the navigation and cross-link shape, so writing them first avoids editing the same paragraphs twice.

## Non-Goals

- Do not rewrite the API reference by hand.
- Do not turn dev-plan history into public product claims.
- Do not broaden AOT/WebAssembly claims beyond the generated SQLite smoke evidence.
- Do not promise full migrations, arbitrary LINQ, broad AOT, OPFS/browser storage, outer joins, multi-joins, or general `GroupBy(...)` support.
- Do not remove support matrices; they are the maintainer evidence behind public claims.
