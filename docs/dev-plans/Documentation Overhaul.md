> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Documentation Overhaul Plan

## Purpose

This document defines the first documentation cleanup pass for DataLinq.

The goal is not to make the docs larger. The goal is to make them trustworthy.

Right now the main problems are:

1. Public docs mix implemented behavior, vague summaries, and future plans.
2. Navigation hides existing pages and exaggerates missing ones.
3. Several pages read like generic ORM documentation instead of verified DataLinq documentation.
4. The onboarding path from install to first successful query is weak.

The correct standard is simple:

- Public docs should describe shipped behavior.
- Roadmap material should be labeled as roadmap.
- Internal architecture docs should not be the only place where users learn how to use the product.

---

## Initial Audit Summary

### High-confidence problems

- `docs/Contributing.md` incorrectly says DataLinq targets `.NET 6 or higher`, while the repo build currently targets `net8.0;net9.0;net10.0`.
- `docs/Contributing.md` still refers to project layout that no longer exists cleanly, including `DataLinq.Core`, a top-level `tests` folder, and a placeholder code-of-conduct URL.
- `README.md` is too shallow for onboarding and does not provide a real "start here" funnel.
- `docs/index.md` is misleading because it lists major areas as `TBD` even when at least some of them already exist.
- `docs/toc.yml` and `docs/index.md` disagree on what the documentation contains.
- `docs/Querying.md` exists but is not in `docs/toc.yml`, so it is effectively orphaned.
- `docs/Querying.md` contains an explicit `TODO` while its summary talks as if the missing section already exists.
- `docs/Caching and Mutation.md` contains user-facing behavior but is categorized as internals.
- `docs/Contributing.md` contains boilerplate and placeholder-style text and should not be treated as authoritative without rewrite.
- `docs/Configuration files.md` needs cleanup and verification; it contains an invalid citation artifact and internal inconsistencies.
- `docs/CLI Documentation.md` omits important behavior, including config-directory discovery, selection failure rules for `-n` and `-t`, and the fact that `create-models` overwrites generated model files.
- `docs/Diagrams.md` is not discoverable from the docs index or docs TOC.
- `docs/Project Specification.md` overclaims current capabilities and reads like a stitched roadmap/spec draft rather than a maintained source of truth.
- Several internal docs still talk as if JSON, CSV, XML, in-memory, or broader non-SQL backends are current platform scope. In the repo today, the concrete providers are MySQL/MariaDB and SQLite.
- `docs/dev-plans/*` contains roadmap/specification material and must not be allowed to blur into normative documentation.

### Documentation classes

The current documentation falls into four categories:

1. **Navigation docs**
   - `README.md`
   - `docs/index.md`
   - `docs/toc.yml`

2. **User docs**
   - `docs/CLI Documentation.md`
   - `docs/Configuration files.md`
   - `docs/Querying.md`
   - `docs/Caching and Mutation.md`
   - `docs/backends/MySQL-MariaDB.md`
   - `docs/backends/SQLite.md`

3. **Internal/reference docs**
   - `docs/Technical documentation.md`
   - `docs/Metadata Structure.md`
   - `docs/Source Generator.md`
   - `docs/Query Translator.md`
   - `docs/Implementing a new backend.md`
   - `docs/Diagrams.md`

4. **Roadmap/spec docs**
   - `docs/Project Specification.md`
   - `docs/dev-plans/*`

### Working classification

This is the current rough classification to guide the rewrite:

| Path | Status | Notes |
| --- | --- | --- |
| `README.md` | Incomplete | Needs a real quickstart path and clearer install/runtime split. |
| `docs/index.md` | Misleading | Too many `TBD` placeholders; does not reflect actual content. |
| `docs/toc.yml` | Incomplete | Misses `Querying.md` and does not match the index. |
| `docs/CLI Documentation.md` | Partial | Command list exists, but depth and examples are thin. |
| `docs/Configuration files.md` | Partial / suspect | Needs verification, cleanup, and better examples. |
| `docs/Querying.md` | Incomplete | Exists, but hidden and unfinished. |
| `docs/Caching and Mutation.md` | Partial | Likely useful, but misplaced and too broad. |
| `docs/backends/MySQL-MariaDB.md` | Partial | Needs fact-checking and better surfacing. |
| `docs/backends/SQLite.md` | Partial | Needs fact-checking and better surfacing. |
| `docs/Contributing.md` | Stale | Contains generic boilerplate, wrong target framework guidance, and placeholder content. |
| `docs/Technical documentation.md` | Needs validation | Useful core concepts, but still mixes current implementation with aspiration. |
| `docs/Metadata Structure.md` | Needs validation | Internal reference; probably salvageable. |
| `docs/Source Generator.md` | Needs validation | Internal reference; useful, but too hand-wavy in key areas. |
| `docs/Query Translator.md` | Needs validation | Strongest internal page, but still risks overclaiming supported query surface. |
| `docs/Implementing a new backend.md` | Needs validation | Good candidate for rewrite into a grounded provider guide. |
| `docs/Diagrams.md` | Orphaned | Keep only if linked and still helpful. |
| `docs/Project Specification.md` | Spec / roadmap | Overclaims current behavior and should not be treated as authoritative docs. |
| `docs/dev-plans/*` | Roadmap | Keep clearly separate from shipped docs. |

---

## Target Information Architecture

The docs should be reorganized around how people actually approach the project.

### 1. Getting Started

- Overview
- Installation
- Configuration Files
- CLI Documentation
- Quickstart

### 2. User Guide

- Querying
- Mutation and Transactions
- Relationships and Caching
- Supported LINQ Surface
- Attributes and Model Definitions

### 3. Providers

- MySQL & MariaDB
- SQLite
- Implementing a New Backend

### 4. Internals

- Technical Documentation
- Metadata Structure
- Source Generator
- Query Translator
- Diagrams

### 5. Contributing

- Contributing Guide

### 6. Roadmap

- Project Specification
- `docs/dev-plans/*`

Roadmap content can stay public if desired, but it must be obviously separated from current behavior.

---

## Execution Plan

### Phase 1: Truth Audit

Goal: classify all current docs and verify them against the codebase.

Tasks:

- Verify install instructions against package/project metadata.
- Verify CLI commands and options against `src/DataLinq.CLI/Program.cs`.
- Verify configuration schema against `src/DataLinq/Config/*`.
- Verify provider claims against the MySQL/MariaDB and SQLite implementations.
- Verify query/mutation claims against tests and implementation.

Deliverable:

- A per-page audit list with one of:
  - accurate
  - stale
  - incomplete
  - misleading
  - roadmap/spec

### Phase 2: Navigation Repair

Goal: make the current good content reachable before writing new content.

Tasks:

- Rewrite `docs/index.md` into a clean overview page.
- Update `docs/toc.yml` to match the actual docs set.
- Add missing discoverability for `Querying.md`.
- Decide whether `Diagrams.md` stays public and link it if yes.
- Improve `README.md` so it points readers into a sane doc path.

Deliverable:

- A coherent documentation entry path from repository root to first-use docs.

### Phase 3: Onboarding Rewrite

Goal: make first-time usage comprehensible without reading internals.

Tasks:

- Rewrite installation guidance.
- Clarify the runtime package story versus the CLI tool story.
- Rewrite configuration docs with valid examples.
- Add a quickstart that covers:
  - install package
  - install CLI
  - configure `datalinq.json`
  - generate models
  - instantiate the database
  - run a first query
  - save a first mutation

Deliverable:

- A user can reach "hello world" without guessing.

### Phase 4: Fill Missing User Guides

Goal: cover the most important user-facing concepts explicitly.

Tasks:

- Expand or rewrite `Querying.md`.
- Split mutation/transaction material into cleaner user-facing topics.
- Add supported LINQ guidance.
- Add attributes/model-definition guidance.
- Add troubleshooting/FAQ for common generator/configuration mistakes.

Deliverable:

- The user guide covers the main development workflow without forcing users into internal docs or test code.

### Phase 5: Internal Docs Cleanup

Goal: keep deep docs, but make them technically honest.

Tasks:

- Review internal docs for overclaims.
- Remove or label speculative sections.
- Tighten language around what exists today versus future direction.
- Link internal docs from user docs only when they genuinely help.

Deliverable:

- Internal docs become reference material, not a substitute for user documentation.

### Phase 6: Roadmap Separation

Goal: stop future plans from pretending to be current features.

Tasks:

- Mark `Project Specification.md` as a specification/vision document if retained.
- Keep `docs/dev-plans/*` explicitly framed as roadmap/work-in-progress.
- Ensure public index pages do not imply roadmap items are implemented.

Deliverable:

- Readers can tell the difference between current capability and future ambition.

### Phase 7: Consistency Pass

Goal: remove the last obvious cracks.

Tasks:

- Check links.
- Check examples.
- Check file names and command names.
- Check terminology consistency.
- Build the docs site and fix any broken navigation or rendering issues.

Deliverable:

- A documentation set that is coherent end to end.

---

## Priority Order

If time is limited, do the work in this order:

1. `docs/index.md`
2. `docs/toc.yml`
3. `README.md`
4. `docs/Configuration files.md`
5. `docs/CLI Documentation.md`
6. `docs/Querying.md`
7. `docs/Caching and Mutation.md`
8. `docs/Contributing.md`
9. Backend docs
10. Internal/reference docs

This order matters. A perfectly written page that nobody can find is still a bad doc.

---

## Parallel Work Strategy

This work parallelizes well, but only if the integration stays centralized.

### Suitable sub-agent slices

1. **Install / CLI / config audit**
   - `README.md`
   - `docs/CLI Documentation.md`
   - `docs/Configuration files.md`
   - packaging and project metadata

2. **User-guide and navigation audit**
   - `docs/index.md`
   - `docs/toc.yml`
   - `docs/Querying.md`
   - `docs/Caching and Mutation.md`
   - backend doc discoverability

3. **Internals and provider audit**
   - `docs/Technical documentation.md`
   - `docs/Source Generator.md`
   - `docs/Query Translator.md`
   - `docs/Implementing a new backend.md`
   - `docs/backends/*`

4. **Roadmap/spec separation**
   - `docs/Project Specification.md`
   - `docs/dev-plans/*`

### Important constraint

Sub-agents are good at auditing bounded slices.

Sub-agents are bad at producing a final documentation voice unless someone integrates the results.

That means parallel audits are useful, but the final rewrites should be merged and normalized by one owner.

---

## Definition of Done

The first documentation overhaul pass is done when all of the following are true:

- The README provides a credible path into the docs.
- The docs index and TOC agree with each other.
- Existing pages are not orphaned.
- Public docs do not describe roadmap items as shipped features.
- A new user can get from install to first query without reading source code.
- A contributor can understand how to run the relevant tests and where the code actually lives.
- Provider-specific behavior is documented where it matters.
- Internal docs stop overclaiming unsupported features.

---

## Working Principle

If a claim in the docs cannot be defended from the codebase, tests, or package metadata, it should be removed, softened, or moved to roadmap/spec material.

Documentation that is confidently wrong is worse than documentation that is missing.

