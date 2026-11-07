# Contributing to DataLinq

This guide is for contributors working on the DataLinq repo itself.

## 1. Repository Setup

For the full fresh-machine setup path, including .NET SDK and Podman/WSL requirements, start with [Dev and Test Environment](contributing/Dev%20and%20Test%20Environment.md).

Clone the repo and work from the solution under `src`:

```bash
git clone https://github.com/bazer/DataLinq.git
cd DataLinq/src
dotnet workload restore
dotnet restore DataLinq.sln
dotnet build DataLinq.sln
```

The repo also carries a root `global.json` that opts `dotnet test` into the .NET 10 `Microsoft.Testing.Platform` runner.

The active solution file is:

```text
src/DataLinq.sln
```

Unless noted otherwise, the command examples below assume your current directory is the repo's `src` folder.

The repo currently targets .NET 8, .NET 9, and .NET 10. The active TUnit suites target `net10.0`.

## 2. Repo Layout

Important directories and projects:

- `src/DataLinq`
  Main packaged runtime project.
- `src/DataLinq.MySql`
  MySQL and MariaDB provider package.
- `src/DataLinq.SQLite`
  SQLite provider package.
- `src/DataLinq.CLI`
  The end-user `datalinq` tool.
- `src/DataLinq.Generators`
  Source generator project.
- `src/DataLinq.Tools`
  Shared tooling used by the CLI and generation pipeline.
- `src/DataLinq.DevTools`
  Shared repo-local `dotnet`/NuGet execution, output parsing, and artifact logging primitives for developer-facing CLIs.
- `src/DataLinq.Dev.CLI`
  Developer wrapper for `doctor`, `restore`, `build`, and `test` with repo-local environment isolation and concise output.
- `src/DataLinq.Testing`
  Shared test infrastructure, seeded harnesses, provider matrix logic, and environment helpers.
- `src/DataLinq.Testing.CLI`
  Cross-platform test infrastructure CLI for local orchestration and matrix runs.
- `src/DataLinq.Tests.Unit`
  Pure in-process TUnit lane.
- `src/DataLinq.Tests.Compliance`
  Cross-provider behavior and SQLite-backed compliance lane.
- `src/DataLinq.Tests.MySql`
  Provider-specific MySQL and MariaDB metadata and type-mapping lane.
- `src/DataLinq.Generators.Tests`
  Generator-focused TUnit lane.
- `src/DataLinq.Tests.Models`
  Shared test models and fixtures used by the active test suites and sample code.
- `docs`
  Project documentation.

## 3. Testing

The supported local entry points are:

- `DataLinq.Dev.CLI` for `doctor`, `restore`, `build`, and direct `dotnet test` wrapping.
- `DataLinq.Testing.CLI` for provider-matrix orchestration, container lifecycle, and suite runs that depend on target aliases or batches.
- `DataLinq.Benchmark.CLI` for benchmark discovery, benchmark execution, and performance-history artifacts.

Detailed tool documentation lives here:

- [Dev and Test Environment](contributing/Dev%20and%20Test%20Environment.md)
- [Internal Tooling](contributing/Internal%20Tooling.md)
- [DataLinq.Dev.CLI](contributing/DataLinq.Dev.CLI.md)
- [DataLinq.Testing.CLI](contributing/DataLinq.Testing.CLI.md)
- [DataLinq.Benchmark.CLI](contributing/DataLinq.Benchmark.CLI.md)
- [AI Assistant Guidance](contributing/AI%20Assistant%20Guidance.md)

### Local feedback loop

Keep the local loop fast. While editing, run very focused tests for the behavior under change. At a coherent stopping point for code changes, run `quick` once. Consider `latest` locally when MySQL/MariaDB behavior is affected, including changes in shared code used by those providers. Otherwise leave server and full-matrix coverage to CI.

Start with a method, class, or small group using a TUnit tree-node filter:

```bash
dotnet run --project DataLinq.Testing.CLI -- run --plan focused --suite unit --filter "/*/*/CacheNotificationManagerTests/*" --output failures
```

Choose the matching suite and, for server-specific regressions, explicit targets. The filter grammar is `/<Assembly>/<Namespace>/<Class>/<Test>`, not the old VSTest syntax. The Testing CLI builds the selected test projects by default; a full solution build is not required before every focused run.

Once the slice is ready, run the local pre-commit check:

```bash
dotnet run --project DataLinq.Testing.CLI -- run --plan quick --output failures
```

When MySQL/MariaDB changes warrant broader local verification:

```bash
dotnet run --project DataLinq.Testing.CLI -- run --plan latest --batch-size 4 --output failures
```

`smoke` remains available for a representative no-Podman signal when useful, but is not an extra mandatory iteration step. Reserve local `full` runs for an explicit investigation or release-verification task. CI owns the normal broader coverage; local checks do not replace the required CI gate or the release aggregate described in [CI Test Lanes](contributing/CI%20Test%20Lanes.md).

Rerun successful checks only when further changes or failures justify it. For documentation-only changes, validate the docs instead of running application test plans.

### Supporting tools

Inspect available plans, suites, targets, and recorded measurements:

```bash
dotnet run --project DataLinq.Testing.CLI -- list
```

Use the Dev CLI when the environment needs diagnosis or an explicit restore/build is needed. Scope build targets to the work:

```bash
dotnet run --project DataLinq.Dev.CLI -- doctor --profile repo
dotnet run --project DataLinq.Dev.CLI -- build src/DataLinq.Tools/DataLinq.Tools.csproj
```

The wrapper resolves explicit target paths from the repo root, so target arguments use `src/...` even when the wrapper is invoked from `src`. Full setup and native Windows sandbox bootstrap instructions live in [Dev and Test Environment](contributing/Dev%20and%20Test%20Environment.md) and [Internal Tooling](contributing/Internal%20Tooling.md).

Use `--output failures` for tests, `--output errors` for compiler/NuGet errors, and `--output diag` when full diagnostic artifacts are needed.

For repeated runs, outer `dotnet run --no-build` reuses the CLI binary only. Keep the Testing CLI's default test-project build while test or product sources are changing:

```bash
dotnet run --no-build --project DataLinq.Testing.CLI -c Debug --framework net10.0 -- run --plan focused --suite unit --filter "/*/*/CacheNotificationManagerTests/*" --output failures
```

Build the CLI in the same configuration first, and rebuild it after changes to its sources or dependencies. Inner `run --no-build` is only for unchanged, already-built test hosts; stale outputs are rejected.

Visual Studio runsettings live under `src`: `tests.quick.runsettings`, `tests.latest.runsettings`, and `tests.all.runsettings`.

For performance work, use [DataLinq.Benchmark.CLI](contributing/DataLinq.Benchmark.CLI.md) for discovery, execution, and comparison rather than invoking BenchmarkDotNet directly.

## 4. Documentation Changes

If behavior changes, update the matching docs:

- user-facing setup and usage docs in `docs/`
- internal architecture docs only when the implementation still matches them
- dev-plan documents only when you are intentionally updating migration or design history

Do not let roadmap material pretend to be shipped behavior. For documentation-only changes, check links and command examples and build the docs when feasible for broad edits. If navigation or presentation changes, inspect generated `_site` output. Use a local HTTP server for browser previews.

## 5. Coding Guidelines

- Use normal .NET naming conventions.
- Keep changes focused and defensible.
- Add tests when changing behavior.
- Add comments only when they explain something non-obvious.
- Prefer updating the shared test harness instead of rebuilding ad hoc fixtures in individual test projects.

## 6. Commits and Pull Requests

For agent-assisted changes, `AGENTS.md` defines staging and authorization. Follow the [agent handoff workflow](contributing/AI%20Assistant%20Guidance.md#4-review-stage-and-hand-off) to inspect outgoing commits and confirm the push remote, destination branch, and PR base.

### Merge strategy

Use merge commits by default for feature and fix PRs, merges between long-lived branches, and final release integration such as `v0.10` into `master`. Preserve individual commits and explicit integration points so the history shows both the development work and how it reached the target branch.

Squash and rebase merges are exceptional choices. Explain the concrete reason and obtain explicit maintainer approval for the exception before using either; do not routinely combine a feature or an entire release into one replacement commit. Do not rebase or force-push shared integration branches.

### Commit messages

Use a concise, descriptive subject, a blank line, and a body explaining the actual change and why it matters. Describe the final behavior and motivation for a reader who has not seen the conversation. Mention meaningful regression coverage when useful and include issue references when appropriate.

Do not copy conversation history, discarded approaches, or a sequence of work-in-progress messages into a commit message. For an explicitly approved squash exception, compose one coherent message for the combined change. Conventional Commit prefixes are not required. Release descriptions, not commit prefixes, feed the generated changelog.

### Pull request descriptions

- Keep PRs small enough to review.
- Explain behavioral changes clearly.
- Mention test coverage and any known gaps.
- If docs changed, say which docs were updated.

## 7. License

DataLinq is released under the [MIT License](../LICENSE.md). By contributing, you agree that your contributions are released under that same license.
