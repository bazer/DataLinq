# Development Plans

> [!WARNING]
> This folder contains roadmap, design, migration, audit, and implementation-history material. It is not normative product documentation unless a page explicitly says otherwise.

## Purpose

`docs/dev-plans` keeps future intent and implementation rationale separate from documentation of shipped behavior.

Use sources in this order:

1. public docs and support matrices for current behavior
2. `CHANGELOG.md` for release boundaries
3. the active roadmap for current priority
4. release implementation plans for execution detail
5. durable design notes for broader options and rationale
6. archived plans for history only

## Status Vocabulary

Active planning pages should use one of these statuses:

- **Active**: a living roadmap or index that is continuously maintained.
- **Proposed**: direction is still being evaluated.
- **Accepted**: direction is approved, but execution has not started.
- **In progress**: implementation is active.
- **Blocked**: a named prerequisite prevents useful progress.
- **Implemented**: the planned boundary shipped; move the implementation record to version history or archive.
- **Superseded**: a newer plan owns the work; archive the old page or leave an explicit redirect.
- **Historical**: retained only for rationale or evidence.

Every active plan should also include a target release or `Unscheduled`, last-reviewed date, prerequisites, exit evidence, and explicit non-goals.

## Active Roadmap

- [Development Roadmap](Roadmap.md)
- [Public Roadmap](../Roadmap.md)

The current version-scoped release plan is [DataLinq 0.10](roadmap-implementation/v0.10/README.md).

## DataLinq 0.10

Required release plans:

- [DataLinq 0.10 Implementation Roadmap](roadmap-implementation/v0.10/README.md)
- [0.10 Implementation Order and Integration Plan](roadmap-implementation/v0.10/Implementation%20Order%20and%20Integration%20Plan.md)
- [0.10 Async Public API Decisions](roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md)
- [0.10 Release Evidence and Closeout Implementation Plan](roadmap-implementation/v0.10/Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md)

Required durable design sources:

- [Async and Lazy Loading](query-and-runtime/Async%20and%20Lazy%20Loading.md)
- [Dependency Injection and Hosting Integration](architecture/Dependency%20Injection%20and%20Hosting%20Integration.md)
- [Schema Validation Hooks](providers-and-features/Schema%20Validation%20Hooks.md)
- [Model Testing and Mocking Support](testing/Model%20Testing%20and%20Mocking%20Support.md)

The release has no pre-authorized stretch goals. [Issue #93](https://github.com/bazer/DataLinq/issues/93) is required generator-correctness work. [Issue #65](https://github.com/bazer/DataLinq/issues/65), tooling/Studio interoperability, migrations, Memory mutation/persistence, broad query expansion, and other later programs are explicitly outside the initial 0.10 boundary.

## DataLinq 0.9 History

DataLinq 0.9 is shipped. Its [implementation roadmap](roadmap-implementation/v0.9/README.md), [implementation order](roadmap-implementation/v0.9/Implementation%20Order%20and%20Integration%20Plan.md), and [release evidence plan](roadmap-implementation/v0.9/Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md) remain historical implementation records. Use the [0.9 release notes](../releases/0.9.md) and changelog for shipped behavior.

## Durable Design Sources

These pages carry broader design reasoning than a single release can implement.

### Query and execution

- [LINQ Parser Architecture Review](query-and-runtime/LINQ%20Parser%20Architecture%20Review.md)
- [Relation-Aware Join API](query-and-runtime/Relation-Aware%20Join%20API.md)
- [Async and Lazy Loading](query-and-runtime/Async%20and%20Lazy%20Loading.md)
- [Set-Based Mutations](query-and-runtime/Set-based%20mutations.md)
- [Relation-Aware Mutation API](query-and-runtime/Relation-Aware%20Mutation%20API.md)
- [Batched Mutations](query-and-runtime/Batched%20mutations.md)
- [Mutation Audit Events](query-and-runtime/Mutation%20Audit%20Events.md)

### Metadata and generation

- [Scalar Converter Support](metadata-and-generation/Scalar%20Converter%20Support.md)
- [Metadata Architecture](metadata-and-generation/Metadata%20Architecture.md)
- [Source Generator Optimizations](metadata-and-generation/Source%20Generator%20Optimizations.md)
- [Create Models Layout Configuration](metadata-and-generation/Create%20Models%20Layout%20Configuration.md)
- [Model Directory Regeneration Workflow](metadata-and-generation/Model%20Directory%20Regeneration%20Workflow.md)

Implemented generator/CLI slices should be read as historical rationale when their status says implemented. They do not become active work merely because the file remains outside `archive/`.

### Providers and features

- [Migrations and Validation](providers-and-features/Migrations%20and%20Validation.md)
- [Schema Validation Hooks](providers-and-features/Schema%20Validation%20Hooks.md)
- [JSON Data Type Support](providers-and-features/JSON%20Data%20Type%20Support.md)
- [Generated Column Support](providers-and-features/Generated%20Column%20Support.md)
- [Check Constraint Metadata Design](providers-and-features/Check%20Constraint%20Metadata%20Design.md)

### Application integration and testing

- [Dependency Injection and Hosting Integration](architecture/Dependency%20Injection%20and%20Hosting%20Integration.md)
- [Model Testing and Mocking Support](testing/Model%20Testing%20and%20Mocking%20Support.md)

The testing plan should keep pure model/relation graph builders distinct from a real memory backend. Neither memory nor LINQ-to-Objects fakes prove SQL-provider behavior.

### Memory persistence

- [JSON Persistence Store Architecture](backends/memory/persistence/json/JSON%20Persistence%20Store%20Architecture.md)

This remains a later architecture source. Memory persistence requires provider-neutral mutation, trustworthy transaction semantics, and a canonical committed-change artifact before it can become release work.

## Direction After 0.10

The leading candidate after 0.10 is tooling-process interoperability:

1. stable tooling contracts separated from CLI, Roslyn, and provider implementation dependencies
2. a versioned, cancelable machine protocol with capability negotiation
3. safe repository/config discovery and explicit secret state
4. deterministic model/schema inspection documents
5. structured validation and conservative-diff results

The following write-path release should build on trustworthy mutable baselines and a provider-neutral mutation plan:

1. set-based update/delete
2. relation-aware mutation
3. call-scoped batching
4. provider bulk execution where evidence supports it
5. structured post-commit audit events

## Incubating Work

These are useful design programs, not current release commitments:

- [DataLinq.Store](DataLinq.Store/README.md)
- [Dependency-Tracked Result and Module Caching](query-and-runtime/Result%20set%20caching.md)
- [Distributed Cache Coordination and CDC](architecture/Distributed%20Cache%20Coordination%20and%20CDC.md)

DataLinq.Store remains a separate companion-project incubation track. Its module, authorization, protocol, sync, persistence, and generated-binding work must not silently expand the 0.10 release.

## Historical Implementation Records

- [0.9 implementation roadmap](roadmap-implementation/v0.9/README.md)
- [0.8 implementation roadmap](roadmap-implementation/v0.8/README.md)
- [Archived roadmap implementation records](archive/roadmap-implementation/README.md)
- [Archived query/runtime plans](archive/query-and-runtime/README.md)
- [Archived metadata/generation plans](archive/metadata-and-generation/README.md)
- [Archived testing plans](archive/testing/README.md)
- [Archived tooling plans](archive/tooling/README.md)
- [Archived performance plans](archive/performance/README.md)
- [Archived platform-compatibility plans](archive/platform-compatibility/README.md)
- [Archived documentation plans](archive/documentation/README.md)

The old aspirational product specification, old application-pattern sketches, query-pipeline abstraction, Remotion replacement plan, pre-source-slot projection design, completed tooling plans, and historical performance audits are context rather than active backlog. Their surviving decisions should be merged into current focused plans before the old pages are mechanically archived.

## Maintenance Rules

- Do not duplicate the active priority order across several files.
- Do not reuse one global phase number for unrelated workstreams.
- Give shared artifacts one owner; other plans should depend on or adapt them.
- Update durable design notes when current code invalidates their “current state” sections.
- Move completed implementation records to the versioned roadmap or `archive/`.
- Keep roadmap claims separate from shipped product docs.
- End every release plan with build, test, package, compatibility, benchmark, and documentation evidence.
