# Intro

Welcome to the DataLinq documentation.

These `master` docs describe the unpublished 0.9 release candidate where explicitly marked. They do not claim that final 0.9 packages exist; use the [changelog](../CHANGELOG.md) for the latest published release boundary and the [0.9 candidate notes](releases/0.9.md) for the pending delta.

These docs are structured to help two kinds of readers:

- developers who are new to DataLinq and need a clear path to first success
- contributors or maintainers who need reference material and internals

If you are new here, do not start by wandering through every reference page in the menu. Start with the guided onboarding path.

## What DataLinq Is

DataLinq is an immutable-first, source-generated ORM for .NET.

Its core trade is simple:

- more work in generation, metadata, and cache structure
- less ambiguity at runtime

That leads to a model where:

- queries return immutable instances
- relations are lazy and cache-aware
- updates happen through mutable wrappers and transactions
- supported LINQ is documented conservatively instead of being hand-waved as "probably works"
- normalized queries are validated against a source-owned SQL or experimental Memory backend before backend work

## Why New Developers Should Care

If you have only used mainstream ORMs, DataLinq can feel a bit different at first.

That is because it is optimizing for:

- predictable reads
- strong generated typing
- cache-aware relation traversal
- clearer mutation flow

It is not trying to be the most permissive ORM in the ecosystem. It is trying to be coherent.

## Start Here

If you want the shortest path to understanding the library, follow this order:

1. [Installation](getting-started/Installation.md)
2. [Configuration and Model Generation](getting-started/Configuration%20and%20Model%20Generation.md)
3. [Your First Query and Update](getting-started/Your%20First%20Query%20and%20Update.md)

That sequence gets you from zero to a real generated model surface and a working query/update loop.

## After That

Once the basics are in place, move into the deeper working docs:

- [Querying](Querying.md)
- [Relations and Joins](Relations%20and%20Joins.md)
- [Caching and Mutation](Caching%20and%20Mutation.md)
- [Diagnostics and Metrics](Diagnostics%20and%20Metrics.md)
- [Transactions](Transactions.md)
- [Scalar Converters and Typed IDs](Scalar%20Converters%20and%20Typed%20IDs.md)
- [Supported LINQ Queries](Supported%20LINQ%20Queries.md)
- [Schema Validation and Diff](Schema%20Validation%20and%20Diff.md)
- [Support Matrices](support-matrices/index.md)
- [Platform Compatibility](Platform%20Compatibility.md)
- [Roadmap](Roadmap.md)

## Documentation Areas

If you already know what you need, jump directly to the major sections:

### Getting Started

- [Installation](getting-started/Installation.md)
- [Configuration and Model Generation](getting-started/Configuration%20and%20Model%20Generation.md)
- [Your First Query and Update](getting-started/Your%20First%20Query%20and%20Update.md)

### Usage

- [Querying](Querying.md)
- [Relations and Joins](Relations%20and%20Joins.md)
- [Caching and Mutation](Caching%20and%20Mutation.md)
- [Diagnostics and Metrics](Diagnostics%20and%20Metrics.md)
- [Transactions](Transactions.md)
- [Supported LINQ Queries](Supported%20LINQ%20Queries.md)
- [Schema Validation and Diff](Schema%20Validation%20and%20Diff.md)
- [Support Matrices](support-matrices/index.md)
- [Platform Compatibility](Platform%20Compatibility.md)
- [Attributes and Model Definitions](Attributes%20and%20Model%20Definitions.md)
- [Scalar Converters and Typed IDs](Scalar%20Converters%20and%20Typed%20IDs.md)
- [Troubleshooting](Troubleshooting.md)

### Providers

- [MySQL & MariaDB](backends/MySQL-MariaDB.md)
- [SQLite](backends/SQLite.md)
- [Memory (experimental)](backends/Memory.md)

### Internals

- [Internals](internals/index.md)
- [Architecture Overview](internals/Architecture%20Overview.md)
- [Data Flow](internals/Data%20Flow.md)
- [Source Generator](internals/Source%20Generator.md)
- [Query Translator](internals/Query%20Translator.md)
- [LINQ Parser Architecture](internals/LINQ%20Parser%20Architecture.md)

### Release and Roadmap

- [Changelog](../CHANGELOG.md)
- [0.9 Release Candidate Notes](releases/0.9.md)
- [Roadmap](Roadmap.md)
