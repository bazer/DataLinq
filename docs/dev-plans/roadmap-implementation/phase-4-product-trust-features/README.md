> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 4: Product Trust Features

**Status:** Planning.

## Scope

This folder tracks the execution plan for the fourth roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 4 is about making DataLinq safer to adopt in real projects:

1. detect schema drift between models and live databases
2. report differences in a way a developer can act on
3. generate conservative SQL diff scripts only after validation is trustworthy
4. keep versioned/snapshot migrations as a later slice, not the first move

## Starting Stance

The repo already has enough metadata machinery to start:

- C# models can become `DatabaseDefinition` graphs
- SQLite, MySQL, and MariaDB can read live database metadata
- SQLite, MySQL, and MariaDB can generate create SQL from metadata
- the active TUnit suites already include metadata-from-server and provider SQL coverage

The missing core is not DDL generation. The missing core is a provider-neutral comparison model that can explain drift accurately.

## Documents

- `Implementation Plan.md`

## Related Plans

- [`../../providers-and-features/Migrations and Validation.md`](../../providers-and-features/Migrations%20and%20Validation.md)
- [`../../Roadmap.md`](../../Roadmap.md)
