# AI Assistant Guidance

This page is the canonical operating guide for AI assistants and coding agents working in the DataLinq repo.

It exists to keep the repo rules in one place instead of letting `.github/copilot-instructions.md`, agent files, and random planning docs drift into competing sources of truth.

## Source of Truth

Use the repo docs in this order:

1. `AGENTS.md`
   Hard repository rules and workflow constraints.
2. `docs/Contributing.md`
   The general contributor guide.
3. This page
   Agent-specific operating guidance and repo conventions.
4. `docs/contributing/Internal Tooling.md`
   The canonical entry point for repo-local developer tooling.

`.github/copilot-instructions.md` should stay a thin compatibility shell that points here instead of trying to be a second project brain.

## Non-Negotiable Repo Rules

- Do not create commits, amend commits, or push changes unless the user explicitly asks in the current conversation.
- When stopping at a coherent commit point, include a suggested commit message at the end of the final response. Use the descriptive style preferred in this repo: concise subject, blank line, then a body explaining the actual change and why it matters.
- Do not publish NuGet packages unless the user explicitly asks in the current conversation.
- New tests belong in the active TUnit test projects, not the removed legacy xUnit projects.
- Documentation must describe shipped behavior, not roadmap material pretending to be shipped.
- For broad documentation work, start with an audit and action plan before making a large batch of edits.
- Prefer sandboxed execution first for build, test, local project CLI, and non-destructive git inspection commands. Do not request sandbox escalation preemptively for `dotnet run`, `dotnet build`, `dotnet test`, `DataLinq.Dev.CLI`, `DataLinq.Testing.CLI`, or `DataLinq.Benchmark.CLI`; retry outside the sandbox only after a sandboxed attempt fails with a likely sandbox, network, cache, or filesystem-permission issue.

## Use the Repo Tools, Not Ad Hoc Commands

If you need to build, restore, or run direct `dotnet test` commands, prefer [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md).

If you need provider-matrix orchestration, container lifecycle management, or batched suite execution, prefer [`DataLinq.Testing.CLI`](DataLinq.Testing.CLI.md).

If you need performance runs, history artifacts, or benchmark comparisons, prefer [`DataLinq.Benchmark.CLI`](DataLinq.Benchmark.CLI.md).

That split matters:

- `DataLinq.Dev.CLI` is the stable wrapper for repo-local `dotnet` execution.
- `DataLinq.Testing.CLI` owns test infrastructure orchestration.
- `DataLinq.Benchmark.CLI` owns the benchmark harness and its artifacts.

Using raw `dotnet`, raw Podman commands, or direct BenchmarkDotNet invocation as the default path is usually the wrong move because it bypasses the repo-local environment and artifact conventions.

## Preferred Workflow

For normal code work:

```bash
dotnet run --project src/DataLinq.Dev.CLI -- doctor --profile repo
dotnet run --project src/DataLinq.Dev.CLI -- restore
dotnet run --project src/DataLinq.Dev.CLI -- build
```

For targeted tests:

```bash
dotnet run --project src/DataLinq.Dev.CLI -- test src/DataLinq.Tests.Unit/DataLinq.Tests.Unit.csproj
```

For provider-matrix or server-backed runs:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- list
dotnet run --project src/DataLinq.Testing.CLI -- run --suite all --alias latest --batch-size 4
```

The compliance quick suite is known to run successfully inside the Codex sandbox on native Windows:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

For benchmarks:

```bash
dotnet run --project src/DataLinq.Benchmark.CLI -- list
dotnet run --project src/DataLinq.Benchmark.CLI -- run
```

## Documentation Boundaries

The repo has several documentation classes. Do not mix them casually.

- `README.md`
  GitHub-facing overview.
- `index.md`
  DocFX website homepage.
- `docs/index.md`
  Documentation intro page.
- `docs/*.md`
  Shipped product and contributor documentation.
- `docs/dev-plans/**`
  Design records, plans, and implementation history.

If you need current behavior, prefer normal docs and code.

If you need future intent, migration context, or implementation rationale, check `docs/dev-plans/`.

If you change docs navigation or site presentation, verify the generated `_site` output. Do not trust source markdown alone.

## Release and Packaging Notes

When the user asks about release workflow:

- prefer `publish-nuget.ps1`
- prefer prompting for secrets at execution time
- do not recommend long-lived NuGet API key storage unless there is a real reason
- do not assume CI-first publishing

When working on release notes:

- treat `CHANGELOG.md` as generated output
- use `generate-changelog.ps1` and the existing release-note workflow instead of manually treating the changelog as the primary authoring surface

## What Still Belongs in `AGENTS.md`

`AGENTS.md` should stay short and opinionated:

- repo rules
- hard workflow constraints
- things an agent must obey even if no one reads the wider docs

It should not become a dumping ground for every project detail, release history bullet, or tool walkthrough. That is exactly how instruction files become stale.
