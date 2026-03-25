# Intro

Welcome to the DataLinq documentation.

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
- [Caching and Mutation](Caching%20and%20Mutation.md)
- [Transactions](Transactions.md)
- [Supported LINQ Queries](Supported%20LINQ%20Queries.md)

## Documentation Areas

If you already know what you need, jump directly to the major sections:

### Getting Started

- [Installation](getting-started/Installation.md)
- [Configuration and Model Generation](getting-started/Configuration%20and%20Model%20Generation.md)
- [Your First Query and Update](getting-started/Your%20First%20Query%20and%20Update.md)

### Usage

- [Querying](Querying.md)
- [Caching and Mutation](Caching%20and%20Mutation.md)
- [Transactions](Transactions.md)
- [Attributes and Model Definitions](Attributes%20and%20Model%20Definitions.md)
- [Troubleshooting](Troubleshooting.md)

### Providers

- [MySQL & MariaDB](backends/MySQL-MariaDB.md)
- [SQLite](backends/SQLite.md)

### Internals

- [Technical Documentation](Technical%20documentation.md)
- [Source Generator](Source%20Generator.md)
- [Query Translator](Query%20Translator.md)
