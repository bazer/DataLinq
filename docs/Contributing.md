# Contributing to DataLinq

This guide is for contributors working on the DataLinq repo itself.

## 1. Repository Setup

Clone the repo and work from the solution under `src`:

```bash
git clone https://github.com/bazer/DataLinq.git
cd DataLinq
dotnet restore src/DataLinq.sln
dotnet build src/DataLinq.sln
```

The repo also carries a root `global.json` that opts `dotnet test` into the .NET 10 `Microsoft.Testing.Platform` runner.

The active solution file is:

```text
src/DataLinq.sln
```

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

Check the local `dotnet` execution profile before you start blaming the repo:

```bash
dotnet run --project src/DataLinq.Dev.CLI -- doctor --profile repo
```

Prefer the developer wrapper for restore/build so the repo uses its local `.dotnet` home, AppData, NuGet cache, and artifact paths:

```bash
dotnet run --project src/DataLinq.Dev.CLI -- restore
dotnet run --project src/DataLinq.Dev.CLI -- build
dotnet run --project src/DataLinq.Dev.CLI -- test src/DataLinq.Tests.Unit/DataLinq.Tests.Unit.csproj
```

Useful output modes:

- `--output quiet`
  One-line success, concise failure summary.
- `--output errors`
  Distinct compiler/NuGet errors only.
- `--output failures`
  Test-failure focused output.
- `--output diag`
  Full diagnostic output with raw artifacts.

List the available targets, aliases, suites, and current runtime state:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- list
```

Run the fast local lane:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- run --suite all --alias quick
```

Run the main latest server-backed lane:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- run --suite all --alias latest --batch-size 4
```

Run only a specific suite:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- run --suite generators
dotnet run --project src/DataLinq.Testing.CLI -- run --suite unit
dotnet run --project src/DataLinq.Testing.CLI -- run --suite compliance --alias latest --batch-size 4
dotnet run --project src/DataLinq.Testing.CLI -- run --suite mysql --alias latest --batch-size 4
```

When invoking the CLI repeatedly from the same build output, prefer `--no-build` together with an explicit configuration and framework:

```bash
dotnet run --no-build --project src/DataLinq.Testing.CLI -c Debug --framework net10.0 -- run --suite all --alias latest --batch-size 4
```

Visual Studio runsettings live under `src`:

- `src/tests.quick.runsettings`
- `src/tests.latest.runsettings`
- `src/tests.all.runsettings`

If you are only changing one slice of the codebase, prefer targeted suite runs over blanket full-matrix execution.

## 4. Documentation Changes

If behavior changes, update the matching docs:

- user-facing setup and usage docs in `docs/`
- internal architecture docs only when the implementation still matches them
- dev-plan documents only when you are intentionally updating migration or design history

Do not let roadmap material pretend to be shipped behavior.

## 5. Coding Guidelines

- Use normal .NET naming conventions.
- Keep changes focused and defensible.
- Add tests when changing behavior.
- Add comments only when they explain something non-obvious.
- Prefer updating the shared test harness instead of rebuilding ad hoc fixtures in individual test projects.

## 6. Pull Requests

- Keep PRs small enough to review.
- Explain behavioral changes clearly.
- Mention test coverage and any known gaps.
- If docs changed, say which docs were updated.

## 7. License

DataLinq is released under the [MIT License](../LICENSE.md). By contributing, you agree that your contributions are released under that same license.
