> [!WARNING]
> This implements the synchronous AAPI-16 prerequisite. Async reference orchestration, generated async navigation and public T10 graph/helper parity remain open. W1 and SQLite W0-F1 are not closed.

# W1 Required Reference Validation

**Recorded:** 2026-09-18. Requirements come from [AAPI-16](Async%20Public%20API%20Decisions.md#aapi-16-required-references-return-a-row-or-fail-in-both-sync-and-async) and [AAPI-55](Async%20Public%20API%20Decisions.md#aapi-55-shared-navigation-state-and-explicit-failure-classes), which already selects `InvalidOperationException` for missing required references and duplicate-target cardinality. This follows the accepted synchronous [relation-enumeration prerequisite](W1%20Relation%20Enumeration%20Integration.md), merged as [PR #111](https://github.com/bazer/DataLinq/pull/111).

## Runtime And Generated Contract

Generated required single-reference getters now validate the one value returned by their existing `IImmutableForeignKey<T>` holder. Missing values throw `InvalidOperationException` identifying the model and property, without raw key values. Optional navigation still returns null. The check follows relation metadata even when generated nullable annotations are disabled. There is no second lookup, no new public member/exception type, and no change to the covariant holder interface or its nullable result contract.

Cold and warm absence therefore both fail required navigation. The holder may cache an absent result under its existing generation/notification protocol, but the generated property never exposes that absence as a successful required result. Invalidation and a later valid target reuse the existing loader. Provider, cancellation, unsupported-capability and cardinality exceptions pass through; they are not caught and reclassified as missing data.

The composite fixture exposed an existing reference bug: `DataLinqKey.IsNull` identifies the null-key sentinel, not every composite key containing NULL. A partially NULL foreign key could enter primary-key loading and fail canonical-key validation. The new internal `ProviderKeyComponents.HasNullReferenceComponent` handles dynamic and typed provider keys specifically for reference resolution. Any NULL component means no target. It does not alter key identity, collection grouping or general key-lookup validation. The reference still validates source/transaction ownership before deciding that its key is absent.

Duplicate non-primary-key references retain the existing `SingleOrDefault` cardinality check. No arbitrary first-row path was introduced. A dedicated SQLite fixture declares a valid unique target in metadata, then deliberately recreates its own temporary target table without the unique constraint (after dropping its empty child table). This tests the real reference loader against schema drift/corrupt data without weakening model validation or disabling constraints globally. It exercises both optional and required navigation, cached key sets, and invalidation between one and two matches.

The [unreleased migration notes](../../../Relations%20and%20Joins.md#010-required-reference-correction-unreleased) explicitly require regeneration/recompilation: updating runtime packages alone cannot replace a getter already compiled into a consumer assembly. Required graphs must be corrected or declared optional. Nullable general `Get` misses remain nullable.

## Development Evidence

The new provider cases cover scalar absent/dangling references, partially NULL composite keys, converted keys, cache reuse and invalidation, continued transaction use after missing-reference failure, busy admission, terminal source fallback, and custom reference holders. The custom fixture preserves exact provider/cancellation/unsupported exception identity, accepts one target and rejects duplicates. It is a focused fixture, not a claim that the planned public T10 graph builder exists.

Two generated-consumer cases compile and execute the generated getter in isolated assembly load contexts with nullable annotations enabled and disabled. They verify the missing-reference exception and exactly one custom-holder access. Two unit cases distinguish reference absence from composite key identity and cover typed provider/scalar components.

Local Release / .NET 10:

- `artifacts/w1-required-references-focused-final.json`: **8/8 passed** (six active-provider cases plus the two SQLite cardinality cases).
- `artifacts/w1-required-references-generator-focused.json`: **2/2 passed**.
- `artifacts/w1-required-references-unit.json`: **2,202/2,202 passed**, full unit suite at maximum parallelism 16.
- `artifacts/w1-required-references-generators.json`: **71/71 passed**, full generator suite at maximum parallelism 8.
- `artifacts/w1-required-references-sqlite-file.json` and `w1-required-references-sqlite-memory.json`: **527/527 passed each**, full compliance anchor runs at maximum parallelism 8.
- `artifacts/w1-required-references-memory.json`: **149/149 passed**, full Memory suite at maximum parallelism 16.
- Unit/dependency, generator, compliance, Memory and core .NET 8/9/10 builds passed with zero warnings/errors. Logs use the `w1-required-references-` prefix; the final compliance log is `compliance-build-complete.log`.
- `artifacts/w1-required-references-docfx-build.log`: DocFX passed with zero warnings/errors; generated `_site/docs/Relations and Joins.html` contains the required-reference correction and regeneration guidance. The planning page's relative links and the diff whitespace check also pass.

Failed development attempts are retained. The first fixture build needed explicit database `[Nullable]` attributes; C# nullable annotations alone do not configure mutable column nullability. A later build correctly rejected the deliberately non-unique foreign-key target, so the cardinality fixture now declares valid metadata and changes only its temporary database schema. The first expanded focused run exposed the partially NULL key bug and SQLite's foreign-key mismatch protection; the runtime fix and corrected isolated schema fixture resolved those failures. An initial focused CLI invocation was rejected because provider-affinity filtering cannot be combined with a custom filter; no tests ran in that invocation.

The PR records exact-head CI. These are behavioral and generated-consumer development checks, not a packed release gate. The locked published baseline remains 0.9.2; the earlier #111 packed report retains only the accepted AAPI-11 binary changes and is not represented as evidence from this later commit. No package publication or baseline rewrite is part of this slice.

## Remaining Work

Internal async reference loading must apply the same nullable result, ownership, completeness and cardinality rules without invoking a synchronous property after awaiting. Independent waiter cancellation and generation-safe row/index/reference publication still need actual suspension/race evidence. W3 owns generated public async navigation and its capability/inheritance/collision consumers; T10 owns public reference/graph helpers. The [full W1 audit](W1%20Completion%20Audit.md) still includes model/cache and expression/prepared execution, non-query/mutation/metadata orchestration, owning-root disposal, complete diagnostics/telemetry and the .NET 10 performance comparison.
