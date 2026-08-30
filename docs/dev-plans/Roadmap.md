> [!WARNING]
> This is an internal planning document. It describes intended work, not shipped behavior. Use the public docs, support matrices, changelog, and release notes for current product claims.

# DataLinq Development Roadmap

**Status:** Active.

**Last reviewed:** 2026-08-30.

## Purpose

This page answers three questions:

1. What is the next release trying to prove?
2. Which work is required or deliberately later?
3. Which detailed plan owns each decision?

DataLinq 0.9 is shipped. Its implementation record remains under [`roadmap-implementation/v0.9/`](roadmap-implementation/v0.9/README.md), while shipped behavior belongs in the public docs, [0.9 release notes](../releases/0.9.md), and changelog.

## Published Baseline

DataLinq 0.9 established the foundation that 0.10 consumes:

- a DataLinq-owned query plan split into structural templates and immutable invocation values
- backend-neutral read-source, row-loading, materialization, and capability-validation seams
- scalar converters and typed IDs across the supported SQL and Memory boundaries
- explicit physical UUID storage codecs for SQLite, MySQL, and MariaDB
- an experimental, provider-free, read-only `DataLinq.Memory` package with a deliberately bounded query contract
- trustworthy SQL mutable-instance baselines across commit, rollback, failure, uncertain completion, and disposal
- release evidence covering packages, API compatibility, provider matrices, constrained runtimes, browser smoke, documentation, and benchmarks

The important current limitations are equally real:

- public database I/O is still primarily synchronous
- DataLinq has no first-class dependency-injection or Generic Host integration package
- unit-of-work and host lifetime ownership are not expressed through one supported application contract
- startup schema validation has no standard host integration
- application tests lack a complete metadata-aware immutable/relation graph and unit-of-work testing surface
- C# source type aliases can escape into generated files without a valid semantic type identity
- full migration execution, provider-neutral set-based mutation, Memory mutation/persistence, and tooling-process interoperability are not shipped

## Roadmap Principles

1. Make ordinary application adoption boring before expanding into another major engine.
2. Use native async provider APIs; never market `Task.Run` or sync-over-async as asynchronous database I/O.
3. Keep I/O explicit. Property access must not secretly issue database commands.
4. Make provider, database, unit-of-work, transaction, and disposal ownership visible in public contracts.
5. Keep business-logic tests, Memory-backed tests, translation tests, and provider tests semantically distinct.
6. Preserve current sync behavior while adding async APIs; compatibility changes require explicit review and evidence.
7. Treat cancellation, logging, metrics, cache invalidation, and transaction terminal states as contract behavior, not plumbing afterthoughts.
8. Do not add optional release work by default. Scope changes require a roadmap revision with dependencies and exit evidence.
9. Measure performance changes against a frozen baseline without letting an unrelated parity target redefine the release.
10. Keep roadmap claims separate from shipped product documentation.

## 0.10 Decision

DataLinq 0.10 is an application-adoption and integration release:

> Make DataLinq a first-class component in modern hosted .NET applications through native asynchronous and cancelable execution, explicit dependency-injection and unit-of-work lifetimes, opt-in startup schema validation, and first-class database-free testing support.

The release does not attempt to combine adoption with migrations, Memory persistence, broad query expansion, Studio protocols, or a new write-path engine. Those are separate programs with different prerequisites and evidence.

The authoritative release-local plans are:

- [DataLinq 0.10 Implementation Roadmap](roadmap-implementation/v0.10/README.md)
- [0.10 Implementation Order and Integration Plan](roadmap-implementation/v0.10/Implementation%20Order%20and%20Integration%20Plan.md)
- [0.10 Async Public API Decisions](roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md)
- [0.10 Release Evidence and Closeout Implementation Plan](roadmap-implementation/v0.10/Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md)

## Required 0.10 Workstreams

### Native Async And Cancellation

Durable design owner:

- [Async and Lazy Loading](query-and-runtime/Async%20and%20Lazy%20Loading.md)

Required outcomes:

- SQLite, MySQL, and MariaDB use provider async APIs, with native asynchronous I/O only where the underlying provider genuinely supports it and explicit SQLite limitations where it does not
- public async query terminals and materialization cover the supported synchronous query families
- relation I/O has explicit async loading rather than hidden async property behavior
- `Transaction()` remains synchronous and lazy; the first database operation initializes through its sync/async path, while mutations, commit, rollback, and disposal have honest async execution
- optional public `CancellationToken` parameters reach initialization and database commands and return cancellation distinctly from timeout or generic failure
- logging, telemetry, cache behavior, materialization, and transaction terminal states remain semantically aligned with sync execution
- existing synchronous APIs remain supported without delegating through `.Result` or `.GetAwaiter().GetResult()`

Awaitable entities, automatic lazy loading, sync property access that triggers I/O, and broad public backend plugin APIs are not part of this workstream.

### Dependency Injection, Hosting, And Unit Of Work

Durable design owner:

- [Dependency Injection and Hosting Integration](architecture/Dependency%20Injection%20and%20Hosting%20Integration.md)

Required outcomes:

- a deliberate extensions package keeps host dependencies out of unrelated runtime packages
- applications can register a generated database and provider through normal `IServiceCollection` composition
- read-root access does not accidentally grant an ambient mutable transaction
- explicit unit-of-work factories own transaction creation, commit, rollback, failure, cancellation, and disposal
- service lifetimes are tested for Generic Host, ASP.NET Core, workers, concurrent scopes, and application shutdown
- DataLinq logging flows through the host logging pipeline with stable categories
- the first release uses explicit ownership rather than an ambient `AsyncLocal` session

The first release supports unnamed registrations only. Named/keyed databases, read replicas, tenant routing, framework-specific XAML packages, and ambient transaction scopes require later evidence.

### Startup Schema Validation

Durable design owner:

- [Schema Validation Hooks](providers-and-features/Schema%20Validation%20Hooks.md)

Required outcomes:

- applications can opt into startup validation through the hosting integration
- fail-fast, warning-only, and disabled policies are explicit
- validation reuses the supported schema comparer and provider metadata readers
- cancellation, timeout, missing-secret, connectivity, metadata-read, drift, and unsupported-difference outcomes are distinguishable
- validation emits structured diagnostics through normal host logging
- startup validation never applies migrations or repairs schema

MSBuild/build-time validation remains outside 0.10. It may later reuse the same structured validation contract, but it is not part of the startup-hosting baseline.

### Testing Support

Durable design owner:

- [Model Testing and Mocking Support](testing/Model%20Testing%20and%20Mocking%20Support.md)

Required 0.10 subset:

- metadata-aware immutable row builders with correct values, primary keys, equality, and `Mutate()` behavior
- complete collection and reference relation test doubles
- relation graph builders that validate relation direction, keys, nullability, and deterministic ordering
- fixture-oriented registration over the real `DataLinq.Memory` capability set
- fake unit-of-work behavior aligned with the real 0.10 unit-of-work contract, including commit, rollback, disposal, and failure injection
- DI replacement helpers for Memory-backed reads, fake units of work, and clearly named SQLite-in-memory provider tests
- documentation that labels exactly what each testing layer proves and does not prove

Generated test-shape interfaces, a broad query-assertion DSL, and simulated provider transaction semantics are later decisions. Builders and doubles must prove whether those additions are necessary first.

### Source Type Alias Correctness

Issue owner:

- [Issue #93: Support source type aliases in generated models](https://github.com/bazer/DataLinq/issues/93)

Required outcomes:

- generator inputs resolve model property types semantically and emit stable, resolvable type identities
- nullability and reference/value classification use the resolved symbol rather than alias spelling
- syntax-only paths fail with a focused diagnostic where semantic alias resolution is unavailable
- changing only an alias target invalidates the affected incremental generator output
- custom scalar-converter aliases and existing keyword/qualified/custom type generation retain regression coverage

This workstream is independent of the adoption dependency chain and may land early. It is not authorization for a broad generator architecture rewrite.

### Release Evidence

Owner:

- [0.10 Release Evidence and Closeout Implementation Plan](roadmap-implementation/v0.10/Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md)

Required outcomes:

- every workstream has focused contract tests before broad integration
- the complete SQLite/MySQL/MariaDB provider matrix remains green
- async and sync paths have semantic parity tests, including cancellation and terminal-state behavior
- new package boundaries pass inspection, consumer smoke, API compatibility, target-framework, and dependency checks
- constrained-runtime and browser evidence is rerun where the affected package graph or runtime surface requires it
- documentation builds cleanly and public pages describe only frozen, verified behavior
- a before/after benchmark receipt explains changed allocations, latency, and telemetry

[Issue #26](https://github.com/bazer/DataLinq/issues/26) remains the historical 0.8 allocation-parity tracker. Literal parity is not a hidden 0.10 release gate. The 0.10 gate is that new regressions are measured, explained, and either corrected or explicitly accepted before release.

## Explicitly Out Of 0.10

- [Issue #65](https://github.com/bazer/DataLinq/issues/65) and broader set-based or relation-aware mutations
- call-scoped batching, provider bulk execution, and structured post-commit audit events
- tooling-process interoperability and DataLinq Studio inspection protocols
- MSBuild/build-time schema validation
- full migration authoring, history, execution, recovery, or repair
- Memory mutation, transactions, forks, persistence, commit logs, replay, or compaction
- broad multi-join, grouping, left-join, or relation-aware join expansion
- automatic structural query-plan caching or dependency-tracked result caching
- generated typed-key output and third-party typed-ID adapters
- SQL JSON-path translation and partial JSON updates
- general observability export protocols or query-shape fingerprints
- PostgreSQL or another new provider
- distributed coordination, CDC, and DataLinq.Store execution

There is no stretch-goal list. Any addition requires an explicit change to this roadmap, the implementation order, and the release-evidence plan before implementation begins.

## Dependency Order

The condensed order is:

1. freeze the 0.9 sync/API/package/provider/performance baseline and inventory every I/O boundary
2. define internal async/cancellation contracts and parity rules
3. implement native provider async execution and terminal-state behavior
4. expose the supported public async query, relation, mutation, and transaction surface
5. build DI registration and explicit unit-of-work ownership on the settled async contracts
6. integrate startup schema validation through the host boundary
7. complete testing builders, Memory registration, fake unit of work, and DI replacement helpers against the real contracts
8. integrate source-alias correctness on its independent lane
9. run provisional package, API, provider, compatibility, documentation, and benchmark evidence
10. freeze one candidate and run final release closeout without feature additions

The detailed gates and safe parallel lanes live in the [0.10 Implementation Order and Integration Plan](roadmap-implementation/v0.10/Implementation%20Order%20and%20Integration%20Plan.md).

## Direction After 0.10

The leading candidate after 0.10 is tooling-process interoperability: versioned, cancelable project discovery, deterministic inspection documents, and structured validation/diff results that external tools can consume without loading arbitrary application assemblies or depending on CLI internals.

The following write-path program remains separately ordered:

1. provider-neutral mutation planning
2. set-based update/delete, including a bounded atomic conditional-update contract
3. relation-aware mutation
4. call-scoped batching and measured provider bulk execution
5. canonical committed-change receipts and structured audit adapters

Migration execution, Memory persistence/replay, observability contracts, broader query work, result caching, PostgreSQL, and DataLinq.Store remain evidence-gated programs rather than implied release commitments.

## Plan Governance

Every active plan must state:

- status
- target release or `Unscheduled`
- last-reviewed date
- prerequisites
- required exit evidence
- explicit non-goals

Use release-local workstream names rather than globally reusing phase numbers. Completed implementation records belong in `roadmap-implementation/<version>/` or `archive/`, not in this active roadmap.

If a durable design note and a release implementation plan disagree, update the durable design or explicitly record the release-specific deviation. Do not allow two files to silently own the same work.

## Review Triggers

Revisit the release boundary only if current evidence shows that:

- the backend-neutral execution foundation cannot carry native cancellation without a material redesign
- provider async semantics cannot preserve cache, transaction, or failure behavior without a public compatibility break
- unit-of-work ownership requires a different provider/database lifetime model than the accepted hosting plan
- testing helpers would need to duplicate Memory or provider semantics rather than adapt the real contracts
- source-alias resolution requires a materially broader generator architecture change
- package/API/benchmark evidence exposes a release-blocking regression

A useful new feature, spare implementation capacity, or a completed required workstream is not by itself a reason to expand 0.10.
