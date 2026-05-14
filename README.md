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
[![Supported targets](https://img.shields.io/badge/targets-SQLite%20%7C%20MySQL%208.4%20%7C%20MariaDB%2010.11%2F11.4%2F11.8-0A7BBB)](https://bazer.github.io/DataLinq/)

[Documentation website](https://bazer.github.io/DataLinq/) | [Getting started](https://bazer.github.io/DataLinq/docs/getting-started/Installation.html) | [Changelog](https://bazer.github.io/DataLinq/CHANGELOG.html)

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

It is currently focused on SQLite, MySQL, and MariaDB for .NET 8, .NET 9, and .NET 10.

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

The CLI is installed as a dotnet tool named `datalinq`:

```bash
dotnet tool install --global DataLinq.CLI
```

Current package and repo builds target .NET 8, .NET 9, and .NET 10.

### Configuration
The CLI reads `datalinq.json` and, if present next to it, `datalinq.user.json`.

Minimal example:

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "SourceDirectories": [ "Models/Source" ],
      "DestinationDirectory": "Models/Generated",
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
datalinq create-models -n AppDb
```

Generated C# files are marked as DataLinq-generated and declare their nullable context. Nullable reference generation is enabled by default; set `"UseNullableReferenceTypes": false` in the database config to opt out.

Validate your configured models against the live database:

```bash
datalinq validate -n AppDb
```

`validate` exits with `0` when no drift is found, `1` when schema drift is detected, and `2` for command, configuration, metadata, or validation issues. Use `--output json` when wiring the result into automation; JSON output includes structured validation `issues` as well as drift `differences`.

Generate a conservative SQL suggestion script for supported additive drift:

```bash
datalinq diff -n AppDb -o update_schema.sql
```

`diff` is read-only. It comments destructive, ambiguous, or unsupported changes instead of applying them.
If validation issues exist, `diff` reports them and writes no SQL file.

If your config contains more than one database, pass `-n`.
If the selected database contains more than one connection type, pass `-t`.

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

- [Website Home](https://bazer.github.io/DataLinq/)
- [Docs Intro](https://bazer.github.io/DataLinq/docs/)
- [Changelog](https://bazer.github.io/DataLinq/CHANGELOG.html)
- [Roadmap](https://bazer.github.io/DataLinq/docs/Roadmap.html)
- [Installation](https://bazer.github.io/DataLinq/docs/getting-started/Installation.html)
- [Configuration and Model Generation](https://bazer.github.io/DataLinq/docs/getting-started/Configuration%20and%20Model%20Generation.html)
- [Your First Query and Update](https://bazer.github.io/DataLinq/docs/getting-started/Your%20First%20Query%20and%20Update.html)

After that, the deeper working docs are:

- [Querying](https://bazer.github.io/DataLinq/docs/Querying.html)
- [Caching and Mutation](https://bazer.github.io/DataLinq/docs/Caching%20and%20Mutation.html)
- [Diagnostics and Metrics](https://bazer.github.io/DataLinq/docs/Diagnostics%20and%20Metrics.html)
- [Supported LINQ Queries](https://bazer.github.io/DataLinq/docs/Supported%20LINQ%20Queries.html)
- [Platform Compatibility](https://bazer.github.io/DataLinq/docs/Platform%20Compatibility.html)
- [Transactions](https://bazer.github.io/DataLinq/docs/Transactions.html)
- [Attributes and Model Definitions](https://bazer.github.io/DataLinq/docs/Attributes%20and%20Model%20Definitions.html)
- [Internals](https://bazer.github.io/DataLinq/docs/internals/)
- [Troubleshooting](https://bazer.github.io/DataLinq/docs/Troubleshooting.html)

### License
DataLinq is open source and distributed under the MIT License. See the [LICENSE](LICENSE.md) file for more details.
