> [!WARNING]
> This integrates the accepted synchronous 0.10 naming correction. It adds no public async API and does not close W1 or the SQLite W0-F1 limitation.

# W1 Relation Enumeration Integration

**Recorded:** 2026-09-18. [PR #111](https://github.com/bazer/DataLinq/pull/111) implements [AAPI-11](Async%20Public%20API%20Decisions.md#aapi-11-asenumerable-enumerates-rows-askeyvaluepairs-names-keyed-enumeration). The original PR was still open against `master`; this integration updates that existing branch for `v0.10`, preserving the later mock, cache-publication and transaction-ownership fixes. The [W1 audit](W1%20Completion%20Audit.md) tracks the remaining work.

## Contract And Migration

The pair-returning instance member is renamed from `AsEnumerable()` to `AsKeyValuePairs()` on `IImmutableRelation<T>`, `ImmutableRelation<T, TKey>` (and its inherited one-parameter form), and `ImmutableRelationMock<T>`. There is no instance alias. Standard LINQ `AsEnumerable()` returns the same relation as `IEnumerable<T>` without loading or creating a snapshot at the call.

The runtime keyed path retains the existing `KeyValueSequence` and its transaction admission through enumeration/disposal. The integration does not restore the original PR's older eager implementation. Keyed enumeration still resolves the related rows' primary keys, including composite keys, and preserves cached row identity and keyed lookup. Existing mock implementations remain complete rather than reverting to the original PR's stubs.

The source audit found inferred `AsEnumerable()` uses in transaction-ownership, concurrent snapshot-publication and mock tests. They now explicitly cover both row and keyed enumeration, so the rename cannot silently turn existing keyed coverage into row-only coverage. Other source uses are standard LINQ over dictionaries, arrays or compiler collections; no generated relation call sites required rewriting. A custom explicit interface implementation and reflection checks cover the renamed contract and absence of an instance alias.

The [unreleased migration notes](../../../Relations%20and%20Joins.md#010-enumeration-migration-unreleased) retain the deliberate source/binary break, recompilation requirement and warning about inferred calls that continue to compile with a different element type. Loading timing is stated separately for the built-in runtime and custom/mock implementations; the public keyed contract does not promise deferred loading.

## Development Evidence

Local Release / .NET 10 results:

- `artifacts/w1-relation-rename-unit-final.json`: **2,200/2,200 passed**, full unit suite at maximum parallelism 16.
- `artifacts/w1-relation-rename-sqlite-file.json` and `w1-relation-rename-sqlite-memory.json`: **519/519 passed each**, full compliance anchor shards at maximum parallelism 8.
- `artifacts/w1-relation-rename-memory.json`: **149/149 passed**, full Memory suite at maximum parallelism 16.
- Unit/dependency, compliance, Memory and core .NET 8/9/10 Release builds passed with zero warnings/errors. Logs use the `w1-relation-rename-` prefix, with `unit-build-final.log`, `compliance-build-final.log`, `memory-build.log` and `core-build.log` suffixes.
- `artifacts/w1-relation-rename-docfx-build.log`: DocFX build passed with zero warnings/errors. The generated `_site/docs/Relations and Joins.html` contains the updated migration and loading/ownership explanation.

The first unit build failed because the working project file lacked the existing `ProviderRegistrationProcess` fixture reference. Restoring the exact `v0.10` reference fixed it; the final PR makes no project-file change. The first full unit run passed 2,199/2,200: the scheduling guard rejected the original PR's global `NotInParallel` annotation. The test uses a unique database and provider-instance-scoped metrics, so the obsolete annotation was removed, not added to the global-resource allowlist. Both failed attempts remain in the local artifacts.

Before merging, the PR records exact-head CI and the packed comparison against the unchanged, locked NuGet.org 0.9.2 baseline. The intentional removed members and newly required interface member must remain explicit ApiCompat findings, not broad suppressions or a rewritten baseline. PackOnly verification is local evidence; nothing is published to NuGet. A report containing the accepted breaks is not a passing compatibility release gate, and unrelated findings require separate disposition.

## Remaining Boundary

AAPI-16 required-reference row-or-failure semantics remain unimplemented/unverified by this slice. Async relation loading, independent waiter cancellation, shared generation-safe publication, native provider adoption, public async declarations and package-consumer release evidence remain with their existing W1/W2/W3/T10 owners. The compatibility baseline stays 0.9.2 and benchmarks stay on .NET 10.
