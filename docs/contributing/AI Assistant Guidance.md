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
- On native Windows, prefer `.\scripts\dotnet-sandbox.ps1 ...` for sandboxed `dotnet` commands. Raw `dotnet` may try to read or write profile-scoped .NET and NuGet state, while the wrapper pins `DOTNET_CLI_HOME`, `APPDATA`, `LOCALAPPDATA`, temp paths, NuGet HTTP cache, NuGet scratch space, and restore config to workspace-local paths. Before rewriting `LOCALAPPDATA`, the wrapper captures the real Podman executable and machine socket paths and exposes them through `DATALINQ_TEST_PODMAN_PATH` and `DATALINQ_TEST_PODMAN_SOCKET`; this is required because the Testing CLI normally resolves Podman from `LOCALAPPDATA`. For `run`, `build`, `test`, `pack`, and `publish`, the wrapper adds `--no-restore` because sandboxed execution should consume existing restore assets instead of trying blocked NuGet.org network calls. Use an explicit restore command when package assets are missing.
- If a sandboxed restore fails with `NU1801` or `NU1101`, or a cold build cannot find package-backed types such as `Spectre.Console` or `System.CommandLine`, the likely problem is a missing workspace-local package cache plus blocked NuGet.org access. Rerun the same restore through `.\scripts\dotnet-sandbox.ps1 restore ...` with sandbox escalation, then retry the build inside the sandbox.
- Blazor WebAssembly builds are a known native-Windows sandbox edge. `src/DataLinq.BlazorWasm/DataLinq.BlazorWasm.csproj` can fail inside the Codex sandbox with `MSB4216` and `MSB4027` from `MarshalingPInvokeScanner`, because MSBuild cannot create/connect to the WebAssembly task host. Full solution builds can also fail early with only `Build FAILED. 0 Warning(s) 0 Error(s)` and no useful raw log. Before calling this a repo regression, verify the same command outside the sandbox.
- The WebAssembly `WASM0001` warnings that appear outside the sandbox are real. They come from `SQLitePCLRaw.provider.e_sqlite3` exposing varargs native SQLite functions such as `sqlite3_config` and `sqlite3_db_config`; the SDK says those calls are unsupported on WebAssembly and would fail at runtime. Do not blanket-suppress them without proving the affected functions are unreachable or changing the WebAssembly SQLite provider story.
- For sandboxed server-backed Testing CLI runs on native Windows, set `DATALINQ_TEST_DB_HOST=127.0.0.1` for the command. The sandbox blocks TCP to the Podman VM host, but loopback reaches Podman's `wslrelay` listeners when the matrix ports are free. The matrix uses `13307` through `13310` specifically to avoid common local MySQL/MariaDB services. Server-backed `run` commands refresh `artifacts/testdata/testinfra-state.json` from the actually running containers, so a targeted verification should not leave the state narrowed to one server target. If server-backed sandbox connectivity looks wrong, first check whether the configured port is owned by `wslrelay`.

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

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- doctor --profile repo
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- restore
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- build
```

For warning or build audits, force compilation instead of trusting an up-to-date incremental build:

```powershell
./scripts/dotnet-sandbox.ps1 restore src/DataLinq.sln -v minimal
./scripts/dotnet-sandbox.ps1 build src/DataLinq.sln -c Debug -v minimal --no-incremental
```

If that solution build fails silently inside the sandbox, build the affected projects explicitly and verify the full solution outside the sandbox before diagnosing source code. The current WebAssembly smoke project is the usual culprit.

For targeted tests:

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- test src/DataLinq.Tests.Unit/DataLinq.Tests.Unit.csproj
```

For provider-matrix or server-backed runs:

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- list
dotnet run --project src/DataLinq.Testing.CLI -- wait --alias latest
dotnet run --project src/DataLinq.Testing.CLI -- run --suite all --alias latest --batch-size 4
```

For sandboxed server-backed verification after the containers already exist:

```powershell
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI --no-build -- run --suite mysql --targets mysql-8.4 --output failures
Remove-Item Env:DATALINQ_TEST_DB_HOST
```

The compliance quick suite is local/SQLite-oriented and is known to run successfully inside the Codex sandbox on native Windows:

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

For benchmarks:

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Benchmark.CLI -- list
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Benchmark.CLI -- run
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
