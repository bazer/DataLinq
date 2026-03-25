# Contributing to DataLinq

This guide is for contributors working on the DataLinq repo itself.

---

## 1. Repository Setup

### 1.1 Clone the Repo

```bash
git clone https://github.com/bazer/DataLinq.git
cd DataLinq
```

The solution file is:

```text
src/DataLinq.sln
```

### 1.2 Required SDK

The repo currently targets:

- .NET 8
- .NET 9
- .NET 10

The test projects currently target `net10.0`.

### 1.3 Restore and Build

```bash
dotnet restore src/DataLinq.sln
dotnet build src/DataLinq.sln
```

## 2. Repo Layout

The important directories are:

- **src**  
  Contains the solution, runtime libraries, CLI, generators, tools, benchmarks, and test projects.

- **src/DataLinq**  
  Main packaged runtime project.

- **src/DataLinq.MySql**  
  MySQL and MariaDB provider package.

- **src/DataLinq.SQLite**  
  SQLite provider package.

- **src/DataLinq.CLI**  
  The `datalinq` tool.

- **src/DataLinq.Generators**  
  Source generator project.

- **src/DataLinq.Tools**  
  Shared tooling used by the CLI and generation pipeline.

- **src/DataLinq.Tests**, **src/DataLinq.MySql.Tests**, **src/DataLinq.Generators.Tests**  
  Test projects. The test stack is currently xUnit, not TUnit.

- **src/DataLinq.Tests.Models**  
  Test models and fixtures used by the test projects.

- **docs**  
  Project documentation.

---

## 3. Testing

Run the main test projects explicitly:

```bash
dotnet test src/DataLinq.Tests/DataLinq.Tests.csproj
dotnet test src/DataLinq.MySql.Tests/DataLinq.MySql.Tests.csproj
dotnet test src/DataLinq.Generators.Tests/DataLinq.Generators.Tests.csproj
```

If you are changing only one slice of the codebase, prefer targeted test runs over blanket `dotnet test` from the repo root.

---

## 4. Documentation Changes

If you change behavior, update the matching docs.

- user-facing setup and usage docs in `docs/`
- internal architecture docs only when the implementation still matches them
- roadmap/spec docs only when you are intentionally updating future design notes

Do not let roadmap material drift into normative documentation.

---

## 5. Coding Guidelines

- Use normal .NET naming conventions.
- Keep changes focused and defensible.
- Add tests when changing behavior.
- Add comments only when they explain something non-obvious.

---

## 6. Pull Requests

- Keep PRs small enough to review.
- Explain behavioral changes clearly.
- Mention test coverage and any known gaps.
- If docs changed, say which docs were updated.

---

## 7. License

DataLinq is released under the [MIT License](../LICENSE.md). By contributing, you agree that your contributions are released under that same license.
