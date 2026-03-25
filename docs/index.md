# DataLinq Documentation Index

This index is meant to help you get to the right document quickly.

---

## Getting Started

### [CLI Documentation](CLI%20Documentation.md)
Command reference for the `datalinq` tool, including config loading, database selection rules, and generation commands.

### [Configuration Files](Configuration%20files.md)
Describes `datalinq.json` and `datalinq.user.json`, including database definitions, connections, and generation settings.

## User Guide

### [Querying](Querying.md)
How runtime queries work, what the current LINQ-focused surface looks like, and how relations are loaded.

### [Caching and Mutation](Caching%20and%20Mutation.md)
Explains the primary-key-first fetch path, row and relation caching, mutation flow, and transaction-aware updates.

### [Supported LINQ Queries](Supported%20LINQ%20Queries.md)
Test-backed overview of which query operators and predicate shapes are currently safe to rely on.

### [Transactions](Transactions.md)
Explains implicit transactions, explicit transactions, attached ADO.NET transactions, and provider-specific caveats.

### [Attributes and Model Definitions](Attributes%20and%20Model%20Definitions.md)
Guide to the public attribute set used to define databases, tables, views, relations, indexes, defaults, and cache behavior.

### [Troubleshooting](Troubleshooting.md)
Common failure modes and the shortest honest path to fixing them.

## Providers

### [MySQL & MariaDB](backends/MySQL-MariaDB.md)
Provider-specific notes for schema reading, SQL generation, type handling, and provider caveats.

### [SQLite](backends/SQLite.md)
Provider-specific notes for SQLite affinity handling, smart type inference, and SQL generation.

### [Implementing a new backend](Implementing%20a%20new%20backend.md)
Technical guide for extending DataLinq with another provider.

## Internals

### [Technical Documentation](Technical%20documentation.md)
Overview of the runtime architecture, cache layers, and how the major pieces fit together.

### [Metadata Structure](Metadata%20Structure.md)
Reference for the metadata model used to represent tables, columns, relations, and generated types.

### [Source Generator](Source%20Generator.md)
Explains the source generator pipeline and the generated model surface.

### [Query Translator](Query%20Translator.md)
Explains the expression translation pipeline that turns supported LINQ query shapes into backend-specific SQL.

## Contributing

### [Contribution Guidelines](Contributing.md)
Guide for contributors working on the repo itself.

## Roadmap and Specs

### [Project Specification](Project%20Specification.md)
High-level design and roadmap-oriented specification material. This is not the best source for verifying current behavior.
