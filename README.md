# DataLinq

[![Latest CI](https://github.com/bazer/DataLinq/actions/workflows/latest.yml/badge.svg?branch=master)](https://github.com/bazer/DataLinq/actions/workflows/latest.yml)
[![Full Matrix Nightly](https://github.com/bazer/DataLinq/actions/workflows/full-matrix.yml/badge.svg?branch=master)](https://github.com/bazer/DataLinq/actions/workflows/full-matrix.yml)
[![Full matrix tests](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/bazer/DataLinq/badge-data/.github/badges/full-matrix-tests.json)](https://github.com/bazer/DataLinq/actions/workflows/full-matrix.yml)
[![Docs](https://github.com/bazer/DataLinq/actions/workflows/static.yml/badge.svg?branch=master)](https://github.com/bazer/DataLinq/actions/workflows/static.yml)
[![NuGet DataLinq.SQLite](https://img.shields.io/nuget/v/DataLinq.SQLite?logo=nuget)](https://www.nuget.org/packages/DataLinq.SQLite/)
[![NuGet DataLinq.MySql](https://img.shields.io/nuget/v/DataLinq.MySql?logo=nuget)](https://www.nuget.org/packages/DataLinq.MySql/)
[![NuGet DataLinq.CLI](https://img.shields.io/nuget/v/DataLinq.CLI?logo=nuget)](https://www.nuget.org/packages/DataLinq.CLI/)
[![License: MIT](https://img.shields.io/github/license/bazer/DataLinq)](https://github.com/bazer/DataLinq/blob/master/LICENSE.md)
[![.NET 8, 9, 10](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4?logo=dotnet)](https://github.com/bazer/DataLinq#installation)
[![Supported targets](https://img.shields.io/badge/targets-SQLite%20%7C%20MySQL%208.4%2F9.7%20%7C%20MariaDB%2010.11%2F11.4%2F11.8%2F12.3-0A7BBB)](https://datalinq.org/)

[Documentation website](https://datalinq.org/) | [Getting started](https://datalinq.org/docs/getting-started/Installation.html) | [0.9 release candidate](https://datalinq.org/docs/releases/0.9.html) | [Changelog](https://datalinq.org/CHANGELOG.html)

DataLinq is an immutable-first, source-generated ORM for .NET. It is built for applications where repeated reads, relation traversal, predictable object state, and cache behavior matter more than having an ORM translate every possible LINQ expression.

The short version: DataLinq moves work into generation and metadata so the runtime can do less guessing.

### Why DataLinq Exists

Most ORMs optimize for convenience first. That is useful, but it often means mutable tracked entities, runtime mapping, hidden query behavior, and late surprises.

DataLinq makes a narrower trade:

- **Generated model surface:** source generators create the concrete immutable and mutable types.
- **Immutable reads:** query results are stable objects, not ambient mutable state.
- **Explicit writes:** updates go through mutable wrappers and transactions instead of hidden dirty tracking.
- **Cache-aware relations:** repeated primary-key reads and relation traversal can reuse cached rows.
- **Honest LINQ support:** documented query shapes are backed by tests; unsupported shapes should fail clearly.
- **Schema trust tooling:** `validate` and `diff` compare generated model metadata against live provider metadata without pretending to be full migrations.

It is currently focused on SQLite, MySQL, and MariaDB for .NET 8, .NET 9, and .NET 10. The unpublished [DataLinq 0.9 release candidate](https://datalinq.org/docs/releases/0.9.html) on `master` also adds an experimental, read-only `DataLinq.Memory` backend for explicitly seeded generated models.

### When It Fits

DataLinq is a strong fit for read-heavy applications, small-to-medium relational databases, generated model workflows, and systems where explicit mutation boundaries are a feature rather than a nuisance.

It is not trying to be a universal EF replacement, a full migration engine, or a provider that translates arbitrary LINQ. That restraint is intentional.

---

## Getting Started

### Installation
Install the provider package that matches your runtime database:

```bash
# MySQL and MariaDB
dotnet add package DataLinq.MySql

# SQLite
dotnet add package DataLinq.SQLite
```

The 0.9 candidate package set adds provider-free, read-only tests and transient state through:

```bash
dotnet add package DataLinq
dotnet add package DataLinq.Memory
```

Memory is intentionally not a SQL emulator or a replacement for provider-backed integration tests. The command above requires a published/pre-release `DataLinq.Memory` version; the candidate notes do not claim that the final 0.9 package is already on NuGet. See the [Memory backend guide](https://datalinq.org/docs/backends/Memory.html) for its exact query and lifecycle boundary.

The CLI is installed as a dotnet tool named `datalinq`:

```bash
dotnet tool install --global DataLinq.CLI
```

Current package and repo builds target .NET 8, .NET 9, and .NET 10.

The 0.9 source generator is built against Roslyn 5.0. Visual Studio users therefore require Visual Studio 2026 version 18.0 or newer; command-line builds require a .NET SDK/compiler host containing Roslyn 5.0 or newer. Runtime target-framework support does not make Visual Studio 2022 a supported 0.9 generator host. See [Platform Compatibility](https://datalinq.org/docs/Platform%20Compatibility.html#compiler-host-compatibility) and Microsoft's [Roslyn package/Visual Studio mapping](https://learn.microsoft.com/en-us/visualstudio/extensibility/roslyn-version-support?view=visualstudio).

### Configuration
The CLI reads `datalinq.json` and, if present next to it, `datalinq.user.json`.

For a new project, start with the config initializer:

```bash
datalinq config init
```

That writes shared structure to `datalinq.json` and local connection details to `datalinq.user.json`. You can also write the files by hand.

Minimal example:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "ModelDirectory": "Models",
      "Connections": [
        {
          "Type": "MariaDB",
          "DataSourceName": "appdb",
          "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=secret;"
        }
      ]
    }
  ]
}
```

Generate your data models directly from your database schema:

```bash
datalinq generate models -n AppDb
```

Generated C# files are marked as DataLinq-generated and declare their nullable context. Nullable reference generation is enabled by default; set `"UseNullableReferenceTypes": false` in the database config to opt out.

Validate your configured models against the live database:

```bash
datalinq validate -n AppDb
```

`validate` exits with `0` when no drift is found, `1` when schema drift is detected, and `2` for command, configuration, metadata, or validation issues. Use `--format json` when wiring the result into automation; JSON output includes structured validation `issues` as well as drift `differences`.

Generate a conservative SQL suggestion script for supported additive drift:

```bash
datalinq diff -n AppDb -o update_schema.sql
```

`diff` is read-only. It comments destructive, ambiguous, or unsupported changes instead of applying them.
If validation issues exist, `diff` reports them and writes no SQL file.

If your config contains more than one database, pass `-n`.
If the selected database contains more than one connection type, pass `-p`.

---

## Code Example

```csharp
using DataLinq;
using DataLinq.MySql;
using MyApp.Models;

var db = new MySqlDatabase<AppDb>(connectionString);

var activeUsers = db.Query().Users
    .Where(x => x.IsActive)
    .ToList();

var user = db.Query().Users.Single(x => x.UserId == userId);
var updatedUser = user.Mutate(x => x.DisplayName = "Updated Name").Save();
```

---

## Documentation

If you want the website-first docs experience, start here:

- [Website Home](https://datalinq.org/)
- [Docs Intro](https://datalinq.org/docs/)
- [Changelog](https://datalinq.org/CHANGELOG.html)
- [Roadmap](https://datalinq.org/docs/Roadmap.html)
- [Installation](https://datalinq.org/docs/getting-started/Installation.html)
- [Configuration and Model Generation](https://datalinq.org/docs/getting-started/Configuration%20and%20Model%20Generation.html)
- [Your First Query and Update](https://datalinq.org/docs/getting-started/Your%20First%20Query%20and%20Update.html)

After that, the deeper working docs are:

- [Querying](https://datalinq.org/docs/Querying.html)
- [Caching and Mutation](https://datalinq.org/docs/Caching%20and%20Mutation.html)
- [Diagnostics and Metrics](https://datalinq.org/docs/Diagnostics%20and%20Metrics.html)
- [Supported LINQ Queries](https://datalinq.org/docs/Supported%20LINQ%20Queries.html)
- [Scalar Converters and Typed IDs](https://datalinq.org/docs/Scalar%20Converters%20and%20Typed%20IDs.html)
- [Platform Compatibility](https://datalinq.org/docs/Platform%20Compatibility.html)
- [Transactions](https://datalinq.org/docs/Transactions.html)
- [Attributes and Model Definitions](https://datalinq.org/docs/Attributes%20and%20Model%20Definitions.html)
- [Memory (experimental)](https://datalinq.org/docs/backends/Memory.html)
- [0.9 Release Candidate Notes](https://datalinq.org/docs/releases/0.9.html)
- [Internals](https://datalinq.org/docs/internals/)
- [Troubleshooting](https://datalinq.org/docs/Troubleshooting.html)

### License
DataLinq is open source and distributed under the MIT License. See the [LICENSE](LICENSE.md) file for more details.
