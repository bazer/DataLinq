> [!WARNING]
> This document is roadmap and design material for the planned DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# Memory Backend Architecture

**Status:** Accepted.

**Release scope:** The 0.9 slice is the read-only preview defined below; mutation and persistence remain later work.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

## Purpose

The memory backend should prove that DataLinq's query-plan architecture is real architecture, not merely a nicer SQL-builder input.

The first useful proof is smaller than a complete in-process database. DataLinq 0.9 should target a generated-model, read-only, AOT-friendly preview that executes an explicit `DataLinqQueryPlan` subset over seeded memory rows.

The blunt rule remains:

> If the memory backend must generate SQL, parse SQL, compile expression trees, or reinterpret arbitrary LINQ to work, the design failed.

## Release Horizons

This design intentionally separates immediate proof from later product ambitions.

| Horizon | Intended scope |
| --- | --- |
| 0.9 experimental preview | Generated startup, explicit seeding, canonical provider-value buffers, primary-key lookup, a small capability-gated query subset, correct model materialization, AOT/browser proof |
| Post-0.9 memory database | Mutation, transactions, constraints, concurrency policy, committed-change receipts, store forks/reset, broader indexes and queries |
| Post-0.9 persistence | Manual and automatic persistence, durability policy, storage adapters, commit logs, replay, compaction, operational tooling |

Later sections preserve useful direction for post-0.9 work, but they are not implicit 0.9 requirements.

## Design Thesis

DataLinq should treat memory as a backend, not as a cache trick and not as SQLite with a different connection string.

The architecture should preserve these contracts:

- generated metadata is the schema
- query parsing and normalization produce a backend-consumable execution request
- backend capabilities are validated before execution
- generated accessors and provider-key shapes are the hot path
- backend rows use canonical provider CLR values
- model-facing `RowData` contains model values
- immutable models are materialized through shared runtime paths
- unsupported query shapes fail with DataLinq-owned diagnostics

The 0.9 preview intentionally omits the mutation contract. Adding memory mutation before the provider/source, row, materialization, cache, and transaction boundaries are genuinely backend-neutral would create a parallel runtime full of SQL-shaped stubs.

## Required Shared Runtime Boundary

A memory query executor alone is insufficient. The shared runtime needs backend-neutral seams for:

- provider/data-source construction
- query execution requests
- capability validation
- provider row reading
- provider-value-to-model-value conversion
- `RowData` and immutable-model materialization
- cache identity and lookup

SQL-specific contracts such as `IDbConnection`, `IDbCommand`, SQL parameter rendering, and database transactions must remain inside SQL adapters. The memory provider must not implement them as throwing placeholders merely to satisfy a SQL-shaped root abstraction.

The execution request must also be self-contained for every advertised projection. If execution still needs the original expression tree to rediscover a selector, that projection is not yet backend-neutral and must remain unsupported by memory.

An illustrative execution seam is:

```csharp
internal interface IQueryPlanBackend
{
    QueryBackendCapabilities Capabilities { get; }

    QueryBackendResult Execute(
        QueryExecutionRequest request,
        QueryInvocationValues values);
}
```

The exact interface is open. The durable decisions are:

- reusable plan/template structure is separate from invocation values
- SQL and memory are adapters behind the same runtime-owned execution boundary
- execution does not secretly retain or reinterpret the original expression
- unsupported nodes are rejected by capability validation, not discovered halfway through enumeration

## Value Representation Boundary

The memory store should keep a separate internal provider-value buffer. It should not redefine existing `RowData` as provider-valued.

The value path is:

```text
model value
    -> scalar converter
    -> canonical provider CLR value
    -> CanonicalProviderValueRow values by ordinal

CanonicalProviderValueRow
    -> reverse scalar converter
    -> model-valued RowData
    -> generated immutable model
```

Provider-specific physical codecs are a separate boundary used by SQL readers, writers, literals, and parameter binding. For example, memory keeps a canonical `Guid`; it does not store MySQL `BINARY(16)` byte order. This distinction matters for typed IDs, enums, UUID formats, dates, and other values whose model type differs from either the canonical or physical representation.

The shared runtime now owns `CanonicalProviderValueRow`; memory must store that type directly rather than introducing the older conceptual `MemoryProviderRow` duplicate. The first spike owns an array plus a primary-key-to-row-ordinal dictionary per table and exposes them internally only through read-only views:

```csharp
internal sealed class MemoryTableState
{
    private readonly IReadOnlyList<CanonicalProviderValueRow> rows;
    private readonly IReadOnlyDictionary<DataLinqKey, int> primaryKeyOrdinals;
}
```

The exact collections should follow measurement. The representation rules should not:

- ordinals, not names, are the hot row-access path
- primary keys are built from canonical provider values
- composite keys use generated/comparable shapes where available
- invalid conversion fails during seed/import with table, column, and row context
- model APIs never observe provider storage bytes or provider-only wrapper values

## Generated Startup And Seeding

The 0.9 store starts from generated/frozen model metadata. It performs no live schema discovery and requires no SQL provider.

Expected preview shape:

```csharp
var db = new MemoryDatabase<AppDb>();

db.Seed(seed => seed
    .Table(x => x.Users)
    .Rows(users));

var rows = db.Query().Users
    .Where(x => x.IsActive)
    .OrderBy(x => x.UserId)
    .ToList();
```

Seed processing should:

1. resolve the generated table and column metadata
2. read model values through generated/runtime-owned accessors
3. convert each value through the shared scalar pipeline to its canonical provider CLR value
4. build a `CanonicalProviderValueRow`
5. validate and index the primary key
6. publish a read-only table state

The preview need only accept explicit, table-shaped seed sources that can be mapped without broad reflection. Anonymous-shape convenience mapping, deterministic key generation, fixture graphs, and relaxed partial-schema modes can wait.

## Query Execution

The 0.9 executor should run a query in these stages:

1. validate the plan against memory capabilities
2. bind invocation values through shared provider conversion
3. resolve the single source table
4. use primary-key lookup when the plan shape allows it
5. otherwise interpret supported predicates over provider-value buffers
6. apply supported ordering and paging
7. compute the supported result operator or projection
8. materialize model-valued rows through the shared runtime

The intended first subset is:

- direct table enumeration
- primary-key lookup
- equality and ordered scalar comparisons
- boolean `&&`, `||`, and `!`
- local scalar `Contains(...)`
- `OrderBy`, `ThenBy`, `Skip`, and `Take`
- `Any`, `Count`, `First`, `FirstOrDefault`, `Single`, and `SingleOrDefault`
- direct scalar projection from one source
- direct anonymous projection from one source when the normalized plan is self-contained

Explicitly later:

- joins
- grouping and aggregates
- relation existence and traversal
- joined projections
- post-paging rewrite equivalents
- arbitrary method evaluation
- arbitrary row-local compiled selectors

Memory may implement fewer query shapes than SQL. It must never become looser by falling back to unrestricted LINQ-to-Objects.

## Capabilities And Raw SQL

Capability metadata is part of the backend contract, not optional polish.

Diagnostics should identify at least:

- backend name
- unsupported plan node or result shape
- source slot/table where relevant
- unsupported value or method kind where relevant
- the nearest supported alternative when one is unambiguous

Raw SQL is categorically unsupported. The memory provider should fail immediately with a provider-capability diagnostic. It must not expose a fake `IDbConnection`, attempt SQL parsing, or route the command through a SQL provider.

## Query Semantics

The memory backend needs explicit semantics for each advertised operator:

- null equality and ordering
- string comparison, case sensitivity, and collation assumptions
- numeric coercion
- date/time comparison
- enums, typed IDs, and canonical `Guid` values without applying provider-specific UUID byte layouts
- membership with null and empty sequences
- ordering stability and deterministic paging
- result-operator error/default behavior

These are memory-backend semantics. They are not automatically the semantic truth for SQLite, MySQL, or MariaDB, and they are not proof that generated SQL behaves identically.

Cross-provider tests are still valuable: they catch accidental divergence in the intentionally shared subset. They do not erase real provider differences in collation, null behavior, type affinity, date functions, constraints, or transactions.

## AOT And Browser Constraints

AOT and browser execution are primary evidence for the feature.

Hard constraints:

- no `Expression.Compile()` in query execution
- no runtime code generation
- no broad reflection fallback for model shape, accessors, or row materialization
- no SQL parser
- no SQLitePCLRaw, native SQLite, OPFS, filesystem, or browser-storage dependency in the baseline package
- no background thread required for correctness
- no dynamic provider discovery for generated models

The browser smoke proves execution, not persistence:

- generated startup
- seed load
- primary-key lookup
- filter
- order/page
- supported projection
- unsupported-query diagnostic

## Relationship To SQLite In-Memory

SQLite in-memory and `DataLinq.Memory` test different things.

SQLite in-memory exercises:

- SQL translation and rendering
- SQLite SQL semantics and type affinity
- the SQLite driver and transaction behavior
- schema and constraint behavior

The DataLinq memory preview exercises:

- backend-neutral query-plan execution
- metadata-driven provider-value storage
- provider/model materialization boundaries
- explicit memory capabilities and semantics
- AOT-safe generated access

Neither makes the other redundant.

## Relationship To The Cache

The memory store is not the cache promoted to database status.

- memory provider state owns canonical provider rows and primary-key indexes
- the DataLinq cache owns materialized immutable instances, relation results, identity reuse, metrics, and eviction

Cache eviction must never remove memory database state. Conversely, reading provider rows should still use the ordinary materialization/cache path where that path is backend-neutral.

## Testing Position

Good uses for the preview:

- focused query-plan and materialization tests
- fast application tests inside the explicitly supported semantics
- samples and demos
- transient browser application state

Bad uses:

- proving SQL translation
- validating migrations or live schemas
- asserting provider collation or date behavior
- asserting server constraints, locking, or concurrency
- replacing transaction or durability tests
- declaring a query portable merely because it passed in memory

Provider-backed compliance and integration tests remain the authority for provider behavior.

## 0.9 Verification

The preview requires:

- seed/model-to-provider conversion tests
- provider-buffer-to-model-`RowData` materialization tests
- primary and composite key normalization tests where supported
- typed-ID and canonical-`Guid` lookup/query tests, plus regressions proving physical UUID byte layouts do not leak into memory rows
- capability rejection tests
- an explicit semantics matrix for every advertised operator
- focused SQLite comparisons for the intentionally shared subset
- strict AOT smoke
- browser/WebAssembly smoke without native or persistence dependencies
- allocation measurements that guide, rather than predetermine, later data-structure choices

The public claim must remain `experimental` and `read-only`.

## Post-0.9: Mutation And Transactions

Mutation is a later architecture stage. It should begin only after:

- the provider/source/materialization boundary is proven by the read-only preview
- mutable instances have trustworthy rollback, failed-write, and cross-transaction lifecycle rules
- provider-neutral mutation requests can replace direct SQL construction in shared runtime paths
- SQLite transaction-isolation behavior is aligned enough for meaningful cross-provider comparison

A plausible future promise is:

> Atomic, isolated, in-process mutations over generated model tables, with no durability beyond the store lifetime unless an explicit persistence layer says otherwise.

Future work may include:

- insert/update/delete staging
- snapshot plus staged-write reads
- primary-key, required-value, uniqueness, and optional relation constraints
- generated/default value policies
- atomic root replacement
- rollback and documented conflict handling
- cache/index invalidation

None of those are 0.9 preview capabilities.

## Post-0.9: Committed Changes And Replayability

A successful future mutation should ideally produce a provider-neutral committed-change receipt. Memory-specific commit batches, JSON logs, audit events, CDC, and later store synchronization can adapt that shared primitive instead of inventing incompatible operation shapes.

A receipt may eventually include:

- before/after store version
- database/schema identity
- ordered insert/update/delete operations
- table and column storage identities
- canonical provider values for keys and changes
- optional caller-supplied diagnostics

This belongs to the mutation boundary, not the JSON serializer. Logs, replay-to-version, and compaction follow only after the committed-change contract is stable and deterministic.

## Post-0.9: Forks, Fixtures, And Persistence

Potential later utilities include:

- store forks for independent mutation scenarios
- reset to a named seed state
- deterministic clocks and generated IDs
- strict and relaxed fixture modes
- snapshot objects for inspection and export
- failure injection for retry/error tests
- manual or automatic persistence adapters

These features are attractive, but they also encourage users to treat memory as a universal database fake. Each should ship with a precise testing boundary.

JSON snapshots are the most plausible first serialization format. Browser persistence, filesystem durability, automatic flush, commit logs, replay, and compaction remain separate later milestones.

## Documentation Shape

Public documentation should distinguish:

- SQL providers: SQLite, MySQL, and MariaDB
- SQLite in-memory: SQLite provider mode
- `DataLinq.Memory`: experimental in-process generated-model backend
- JSON snapshot encoding: optional memory-state serialization
- future persistence stores: not shipped until their durability semantics are proven

Good 0.9 wording:

> `DataLinq.Memory` is an experimental read-only backend for generated DataLinq models. It supports explicit seeding, primary-key lookup, and a documented query subset, and is designed for query/runtime tests, examples, and transient browser scenarios.

Avoid:

- "SQL-compatible in-memory database"
- "drop-in replacement for any provider"
- "all LINQ works in memory"
- "default testing database"
- "full ACID in memory"
- "browser persistence"

## Open Questions

- What is the smallest backend-neutral provider/source interface that avoids SQL-shaped throwing stubs?
- Which projection forms can become fully self-contained in the normalized execution request for 0.9?
- Should direct construction use `MemoryDatabase<TDatabase>` or a smaller preview factory?
- Which string comparison is the least surprising documented memory default?
- Should direct scalar comparisons operate on canonical provider values exclusively, or use column-specific comparers supplied by metadata?
- Which query shapes beyond primary-key lookup, filter, ordering, paging, and basic result operators earn 0.9 scope?
- What evidence should graduate the package from experimental after 0.9?

## Non-Goals

- arbitrary LINQ execution
- raw SQL execution
- SQL semantic compatibility
- SQLite compatibility quirks
- replacing provider-backed integration tests
- mutation or transactions in 0.9
- store forks or reset APIs in 0.9
- durable persistence in the baseline package
- commit batches, replay, or compaction in 0.9
- OPFS, IndexedDB, or browser storage in 0.9
- cross-process sharing
- distributed cache coordination
- full migration execution
