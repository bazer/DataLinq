> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation unless a document explicitly says otherwise.

# Roadmap Implementation

## Purpose

The active roadmap answers what should happen next and why. Versioned implementation plans answer what will change, in what dependency order, and which evidence closes the work.

## Release Lines

| Release | Status | Scope |
| --- | --- | --- |
| [0.8](v0.8/README.md) | Implemented and released | DataLinq-owned parser/query plan, Remotion removal, bounded query composition/joins/grouping, constrained AOT/browser evidence, and release closeout |
| [0.9](v0.9/README.md) | Accepted planning direction | backend/source execution foundation, scalar converters and typed IDs, UUID storage correctness, read-only memory preview, SQL-provider correctness gates, and exact release evidence |

The 0.9 baseline deliberately excludes memory mutation, durable JSON persistence, commit logs/replay, production plan caching, and broad join/grouping expansion. One optional stretch may be selected only after the required release work is green.

## 0.9 Implementation Plans

Required:

- [Implementation Order and Integration](v0.9/Implementation%20Order%20and%20Integration%20Plan.md)
- [Query Backend and Execution Foundation](v0.9/Query%20Backend%20and%20Execution%20Foundation%20Implementation%20Plan.md)
- [Scalar Converters and Typed IDs](v0.9/Scalar%20Converters%20and%20Typed%20IDs%20Implementation%20Plan.md)
- [Read-Only Memory Backend](v0.9/In-Memory%20Database%20Implementation%20Plan.md)
- [UUID Storage Format Support](../providers-and-features/UUID%20Storage%20Format%20Support.md)
- [SQL Transaction and Mutable Lifecycle](v0.9/SQL%20Transaction%20and%20Mutable%20Lifecycle%20Implementation%20Plan.md)
- [SQLite Transaction Isolation Alignment](../providers-and-features/SQLite%20Transaction%20Isolation%20Alignment.md)
- [Mutable Instance Lifecycle](../query-and-runtime/Mutable%20Instance%20Lifecycle.md)
- [Release Evidence and Closeout](v0.9/Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md)

Optional stretch candidates:

- [Join Continuation](v0.9/Join%20and%20Grouping%20Continuation%20Implementation%20Plan.md)
- [Snapshot-Only JSON Prototype](v0.9/Memory%20JSON%20Persistence%20Implementation%20Plan.md)

## Version-Scoped Planning Rules

- New release execution work belongs under a version folder.
- Use named workstreams and dependencies instead of continuing global phase numbers.
- A workstream owns its artifact; dependent plans should reference it rather than restating it.
- Durable architecture belongs in the relevant topic folder, while release-specific sequencing and gates belong here.
- Completed execution records stay in their version folder or move to `archive/`.
- Every release ends with build, test, package, compatibility, benchmark, and documentation evidence.

## Historical Records

The 0.8 folder is the authoritative implementation record for the parser-removal, query-composition, AOT/browser, and release-hardening sequence. Older completed global-phase records live under [the archived implementation index](../archive/roadmap-implementation/README.md).

Do not copy that history back into the active roadmap. If current behavior is needed, use the changelog, public docs, support matrices, and current code/tests.
