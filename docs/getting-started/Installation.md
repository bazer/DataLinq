# Installation

This is the recommended path for a new developer:

1. install the runtime package for your database
2. install the `datalinq` CLI
3. generate models from your schema
4. start querying and mutating through the generated model surface

## Choose a Provider

DataLinq 0.9 ships with two SQL provider packages and one experimental provider-free read backend:

- `DataLinq.MySql`
  - use this for both MySQL and MariaDB
- `DataLinq.SQLite`
  - use this for SQLite
- `DataLinq.Memory`
  - use this for explicitly seeded, read-only in-process models when SQL behavior is not under test

There is no separate MariaDB runtime package. MariaDB support lives in `DataLinq.MySql`.

## Install the Runtime Package

For MySQL or MariaDB:

```bash
dotnet add package DataLinq.MySql
```

For SQLite:

```bash
dotnet add package DataLinq.SQLite
```

For the experimental read-only Memory backend:

```bash
dotnet add package DataLinq
dotnet add package DataLinq.Memory
```

Keep DataLinq package versions aligned. Memory is not a replacement for provider-backed tests; see [DataLinq.Memory](../backends/Memory.md) for its exact query and lifecycle boundary.

## Install the CLI

The CLI is used for configuration-driven tasks such as model generation and schema SQL generation.

```bash
dotnet tool install --global DataLinq.CLI
```

## Target Frameworks

Current package and repo builds target:

- .NET 8
- .NET 9
- .NET 10

If your application is on an older target framework, do not assume compatibility just because many .NET libraries happen to support it. DataLinq currently does not document that as a supported path.

## What to Do Next

After installation, move straight to:

- [Configuration and Model Generation](Configuration%20and%20Model%20Generation.md)

That is where the real onboarding starts, because DataLinq becomes useful once your generated model surface exists.
