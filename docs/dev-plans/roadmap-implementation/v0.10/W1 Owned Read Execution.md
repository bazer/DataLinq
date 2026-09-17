> [!WARNING]
> Internal 0.10 integration work. This does not deliver public async support, complete W1, or close W0-F1.

# W1 Owned Read Execution

**Recorded:** 2026-09-17. W1.2 follow-through after [PR #150](https://github.com/bazer/DataLinq/pull/150), under the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception).

## Private Hydration Authority

The previous synchronous authoritative-row reload permitted reads whenever the caller ran on a recorded thread. A deterministic provider-command callback reproduced the resulting admission hole: public `transaction.Query()` succeeded while hydration still belonged to an active mutation. The negative control is retained in `artifacts/w1-owned-read-before.json` (one expected failing test).

Hydration now takes an explicit step from the mutation's existing operation lease. That step travels through the table cache, canonical row loader and materialization cache, or through the provider-sensitive fallback query path. Each owned boundary validates the step against the original transaction's active gate. A released, superseded or foreign step cannot authorize even a warm cache read. The thread ID field and its bypass are removed.

The private row-service bundle is not installed as an entity's read source. Generated construction and transaction-local cache keys retain the original transaction. Provider callbacks, converters, model constructors and application notifications do not receive the step; ordinary public calls still face normal admission. Tests exercise provider construction/read/cleanup callbacks, successful canonical and binary-key fallback hydration, cached instance identity, and the later transition to committed reads.

## Synchronous Read Boundaries

`TransactionReadScope` owns a lease and active step. It rechecks transaction lifecycle after admission, keeps the slot through resource cleanup, and releases it on every scope exit. Nested private reads validate the supplied step without acquiring or releasing the enclosing operation. No monitor is held across provider calls, model construction or callbacks.

| Integrated boundary | Ownership lifetime |
| --- | --- |
| Single-row table-cache lookup | Before warm/cold cache probing through loading, conversion, materialization, publication and metrics |
| Canonical single/batch/index source-row loader | Query construction through buffered decoding and reader/command disposal |
| Scalar-key fallback / `Select.ReadFirstRow` | Command construction through row decoding and disposal; private hydration retains its enclosing owner |
| `Select.ExecuteScalar` (typed and object) | Command construction through result and command/telemetry cleanup |
| `Select.ReadReader` / `ReadRows` | First enumeration move through reader/command cleanup, retaining admission between rows |
| `Transaction.GetFromQuery` / `GetFromCommand` | First enumeration move through row construction and reader cleanup, retaining admission between rows; caller-supplied command remains borrowed |

Construction of these deferred sequences/enumerators remains cold. Busy reads, commit, rollback and caller disposal fail without tearing down the active operation. Read-only database roots do not acquire transaction gates. Public signatures and provider packages are unchanged; execution remains synchronous.

## Development Evidence

The added cases cover the regression, successful canonical/fallback hydration, warm and cold lookup rejection, stale/foreign steps, preserved source/cache identity, cold sequence construction, early exit, exhaustion, read failure, cleanup failure, borrowed-command ownership, cancellation before dispatch, command-creation failure, and another thread attempting admission while reader disposal is deliberately paused. Stable-identity tests stop background cache maintenance locally; existing maintenance/publication race tests remain active.

The eighteen added cases pass. Local development artifacts (CI is recorded separately in the PR):

- `artifacts/w1-owned-read-focused.json`: **119/119 passed**, transaction-focused Release / .NET 10 tests.
- `artifacts/w1-owned-read-unit.json`: **1,908/1,908 passed**, full Release / .NET 10 unit suite.
- `artifacts/w1-owned-read-sqlite-file.json` and `artifacts/w1-owned-read-sqlite-memory.json`: **516/516 passed each**, compliance anchor shards at maximum parallelism 8.
- `artifacts/w1-owned-read-build.log`, `artifacts/w1-owned-read-compliance-build.log` and `artifacts/w1-owned-read-core-build.log`: zero-warning/error builds; core targets .NET 8/9/10.

These are modified-checkout development checks, not a clean release capture. Original W0 evidence and the 0.9.2 compatibility baseline are unchanged.

## Remaining Work

This slice is deliberately not universal operation ownership: it protects the integrated inner boundaries, not an entire outer invocation or buffered result lifetime. The subsequent [query and relation ownership integration](W1%20Query%20and%20Relation%20Ownership.md) extends synchronous admission across those outer sequences and rejects overlapping enumerator calls. Complete invocation capture, async reader transfer, combined cancellation and async enumeration cleanup remain W1.2/W1.4 work. Direct low-level `DatabaseAccess` still bypasses managed orchestration; its provider adapter work remains pending.

W1.3 must establish connection trust, interrupted-read versus write recovery, terminal outcomes and primary/secondary failure composition. A released gate after a cleanup exception is **not** proof that a provider connection can safely be reused; this slice preserves existing synchronous failure behavior and tests admission release separately from trust. Helper callback admission/draining and production lazy initialization are also still pending.

The additional scopes, private service bundles and validation locks have not yet been measured against W0. Coordination/performance acceptance remains required. Native provider async bindings, public API freeze and SQLite acceptance remain blocked by their recorded gates; these passing SQLite checks do not close W0-F1.
