# DataLinq Copilot Instructions

This file is intentionally thin.

The old model where this file tried to act as a giant project memory dump was sloppy and guaranteed to go stale. The canonical instructions now live in normal repo docs.

## Read These First

1. `/AGENTS.md`
   Hard repository rules and workflow constraints.
2. `/docs/Contributing.md`
   General contributor workflow.
3. `/docs/contributing/AI Assistant Guidance.md`
   Canonical operating guidance for AI assistants and coding agents.
4. `/docs/contributing/Internal Tooling.md`
   Canonical map for repo-local developer tooling.
5. The per-tool pages:
   - `/docs/contributing/DataLinq.Dev.CLI.md`
   - `/docs/contributing/DataLinq.Testing.CLI.md`
   - `/docs/contributing/DataLinq.Benchmark.CLI.md`

## Hard Reminders

- Do not create commits, amend commits, or push changes unless the user explicitly asks.
- Do not publish NuGet packages unless the user explicitly asks.
- Put new tests in the active TUnit projects, not the removed legacy xUnit projects.
- Use the repo-local CLIs instead of ad hoc `dotnet`, Podman, or BenchmarkDotNet workflows when the wrapper tools fit the task.
- Do not present `docs/dev-plans/` material as shipped behavior.
- Keep the docs entry points separate:
  - `README.md` for GitHub
  - `index.md` for the DocFX site homepage
  - `docs/index.md` for the documentation intro

## Quick Direction

- Need restore/build/direct test execution: use `DataLinq.Dev.CLI`
- Need provider-matrix orchestration or test infra: use `DataLinq.Testing.CLI`
- Need benchmark runs or comparisons: use `DataLinq.Benchmark.CLI`
- Need future intent or design history: look in `docs/dev-plans/`

If this file starts growing into another giant summary again, that is the signal to fix the real docs instead of stuffing more text in here.
