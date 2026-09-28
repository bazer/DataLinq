# W3 Public Async Surface

**Status, 2026-09-28:** implementation started from W2 merge `69a2b2ee40b25862590fa5206901a51aabfbed80`. The user authorized one draft PR from `codex/0.10-w3` to `v0.10`, with incremental commits and pushes collected there. W3 completion, review and merge remain separate checkpoints.

**PR:** [#230, Implement W3: public async surface](https://github.com/bazer/DataLinq/pull/230).

## Scope And Accepted Boundaries

The [API decisions](Async%20Public%20API%20Decisions.md) and [signature inventory](Async%20Signature%20Inventory%20and%20Compatibility%20Matrix.md) remain the contract. W3 exposes the W1/W2 execution machinery, adds public/generated documentation and verifies real consumer compatibility. Existing synchronous APIs remain supported subject to the already approved keyed-enumeration rename and required-reference correction.

**The user explicitly retained runtime schema validation in W5.** Inventory A05-A07, their supporting validation types, comparison/Include/empty-schema policy and runtime-validation evidence remain W5. Hosting and unit-of-work integration remain W4. B16 is a W4/W5 integration obligation, not a reason to implement validation or hosting in W3. W3 owns the existing metadata/probe/provisioning public async families that those later waves will use.

Further performance investigation, optimization and acceptance remain deferred to W8 after feature implementation. Preserve W0/W1/W2 evidence. Continue functional, telemetry and required CI checks. Official SQLite package adoption and affected verification remain tracked follow-ups; W3 does not change package pins or pooling policy by implication.

**Continuation instruction, 2026-09-28:** complete the remaining W3 work and continue committing/pushing to PR #230. Local MySQL/MariaDB checks use only the latest LTS target for each family: currently `mysql-9.7` and `mariadb-12.3` in `test-infra/podman/matrix.json`. Other server versions are left to CI. The earlier W3.1 all-version local run is historical; do not repeat it under this instruction. Benchmarks remain deferred until the end of 0.10 after all feature waves.

## Opening Source Reconciliation

The checkout at the W2 merge provides the following implementation paths. This maps the accepted families to existing machinery; it is not an emitted public API manifest or a compatibility pass.

| Inventory | Existing implementation and W3 responsibility |
| --- | --- |
| Q01-Q12 | `ExpressionPlanQueryable.Async.cs` captures and executes internal sequences/terminals; `PreparedQuery.Async.cs` exposes only internal execution. Add the complete accepted public query family and prepared entry points without expanding backend translation. |
| R01-R12, K01-K05 | Internal relation/foreign-key loading, `AsyncModelLookup`, `TableCache.AsyncRowLookup` and Memory `FindAsyncCore` provide execution. Add public capabilities, concrete/default dispatch, generated navigation/key helpers and collision diagnostics. `AsKeyValuePairs()` already exists; preserve its migration evidence. |
| M01-M10, T01-T05 | Database/transaction mutation and completion partials provide internal execution and ownership. Add public and generated overloads, callback families and owning-root async disposal; preserve legacy subclass behavior. |
| L01-L17 | Access/reader, fluent selection and raw-model partials provide internal execution. Add public class/interface capabilities and exact reader/command lifetimes without synchronous fallback. |
| A01-A04, A08-A11 | Native provider administration, metadata and provisioning partials supply internal execution. Expose compatible public defaults and built-in dispatch. A05-A07 remain W5. |
| D01-D07, C01-C02 | W1/W2 maintain internal failure contexts and recovery settings. Add immutable public mapping, exact enum values, exception/transaction access and compatible options constructors. |
| B01-B15 | Existing API/package tooling and locked 0.9.2 baseline are inputs. Actual emitted declarations, generated consumers, old binaries and .NET 8/9/10 packed consumers still need W3 evidence. B16 remains W4/W5. |

## Implementation Order

1. **W3.1 — Query execution:** Q01-Q12, optional final tokens, expression predicates, exact numeric/nullability overloads, provider rejection and prepared capture. Add the accepted conditional async-LINQ dependency and focused public-call coverage.
2. **W3.2 — Lookup and relations:** K01-K05 and R01-R11, acyclic shared defaults and concrete/custom dispatch, required/optional/cardinality behavior and existing migration coverage.
3. **W3.3 — Mutations and completion:** M01-M09 and T01-T05, public callback binding, ownership and disposal with old/custom implementations.
4. **W3.4 — Generator:** R12/M10 and typed key bridges, public model-base navigation, DLG004, generated consumer compilation and migration examples.
5. **W3.5 — Lower-level and administrative APIs:** L01-L17, A01-A04 and A08-A11, real built-in execution and explicit unsupported legacy defaults.
6. **W3.6 — Diagnostics and options:** D01-D07/C01-C02, immutable mapping, original exception identity, constructor binding and captured recovery configuration. Implement prerequisites earlier when a preceding slice needs them.
7. **W3.7 — Public contract closeout:** reconcile every accepted declaration and exclusion, compile generated and packed consumers, run ApiCompat against 0.9.2, verify .NET 8/9/10 dependency/import behavior and old/custom binaries, and complete API XML docs and focused examples. Record W4/W5 dependencies separately.

Each slice updates this record with actual changes, commands, results and remaining limitations. Targeted tests establish the slice; the final public contract and broad provider evidence establish W3 completion. No package publication is authorized by this workflow.

## Exit Evidence Still Required

- Actual compiled signature manifest, including constraints, nullable metadata, optional values, parameter names, constructors, default-interface dispatch and stable enum values.
- B01-B15 consumer evidence, with approved changes individually dispositioned and no blanket compatibility suppressions.
- Public-path functional and cancellation/ownership evidence across SQL providers and the bounded Memory subset; no unsupported capability may silently use synchronous I/O.
- Generated source and positive/negative compilation evidence, including overload binding and DLG004.
- XML/API documentation, concrete usage and migration examples, required CI and review of the single W3 PR.

The initial planning commit `a25106ea` contains no new public APIs and does not claim any exit gate complete.

## W3.1 Query Implementation Checkpoint

Implemented Q01-Q12: 41 public query extension declarations on `DataLinq.Linq.DataLinqAsyncQueryableExtensions`, plus scalar/row and sequence prepared `ExecuteAsync` entry points. The numeric family has ten Sum and ten Average overloads and unconstrained generic selector Min/Max. Predicates remain expressions; no additional terminal family or backend translation is introduced.

The wrappers call the existing W1/W2 execution paths. They validate the actual provider, including a public DataLinq query wrapper around a foreign provider, and preserve invocation/enumerator capture and cleanup boundaries. Ordinary sequences capture per enumerator; prepared sequences capture at ExecuteAsync; list/array materializers capture before suspension and return only after owned cleanup succeeds. Public XML comments describe buffering, token combination, disposal, SQLite limits and deliberate imports.

Core now references the centrally pinned `System.Linq.AsyncEnumerable 10.0.12` transitively for .NET 8/9 only. .NET 10 uses the framework implementation. A separate [consumer fixture](../../../../tests/fixtures/PublicAsyncQueryConsumer/README.md), without friend access, compiles and executes extension/static/alias calls and standard local async LINQ. It references source projects, so it is not B14 packed-consumer completion.

### Verification

These are development-working-tree checks, not frozen-candidate release evidence. The provider summary deliberately reports `ValidForEvidence=false` for the development checkout/runner identity; passing case counts are not relabeled as a clean release receipt.

| Check | Result |
| --- | --- |
| Core and separate consumer build, Debug, .NET 8/9/10 | Passed, zero warnings/errors |
| Focused unit query cases, `/*/*/*/*AsyncQuery*` | 92/92 passed |
| Quick plan | 4,815/4,815 passed: generators 71, unit 3,970, Memory 223, SQLite-file compliance 551; zero failures/skips |
| Public query compliance across all eight SQL targets | 32/32 passed: four cases each on SQLite file/memory, MySQL 8.4/9.7 and MariaDB 10.11/11.4/11.8/12.3 |
| Separate consumer execution | Passed on actual .NET 8.0.31, 9.0.20 and 10.0.12 runtimes |
| Whitespace/error check | `git diff --check` passed |

Focused run: `20260928T143914936Z-199edd9de8d644ea946a82311f18d553`. Quick run: `20260928T144024905Z-a6d69210b220483492a798869b24f2c3`. Provider run: `20260928T144219232Z-abf64c09ec3b47c99ff3e8e5a07fca3f`, summary `artifacts/w3-query-provider-summary.json`. TRX/raw output remains under `artifacts/test-results/<run-id>/`.

The initial .NET 9 consumer launch failed because the machine had no 9.x runtime. Installed only a workspace-local runtime under `artifacts/runtimes/dotnet9-9.0.20` from Microsoft's official 9.0 release metadata, checking the archive's SHA-512 against that metadata before extraction. Executing the net9.0 DLL with that runtime passed; no major-version roll-forward is counted as .NET 9 evidence.

Reproduction from the repository root:

```powershell
.\scripts\dotnet-sandbox.ps1 build src/DataLinq/DataLinq.csproj -c Debug -v minimal
.\scripts\dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/*/*AsyncQuery*" --output failures
.\scripts\dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI --no-build -- run --plan quick --output failures
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI --no-build -- run --suite compliance --alias all --filter "/*/*/PublicAsyncQueryTests/*" --output failures --summary-json artifacts/w3-query-provider-summary.json
```

The consumer README contains its build/run commands. New tests cover public predicate and numeric binding, null argument names, foreign-provider rejection, entity/scalar/anonymous projections, empty/default/cardinality results, supported Memory terminals and explicit rejection, buffered cancellation and unchanged transaction usability. Existing controlled query tests now exercise public ordinary/prepared entry points; list and array materializers both cover capture before suspension and cleanup failure without partial success.

### Usage In The Development Surface

```csharp
using DataLinq.Linq;

var query = database.Query().Departments.OrderBy(row => row.DeptNo);
var rows = await query.ToListAsync(cancellationToken);
var exists = await query.AnyAsync(row => row.DeptNo == "d001", cancellationToken);

await foreach (var row in query.AsAsyncEnumerable(cancellationToken))
{
    Console.WriteLine(row.Name);
}

var byKey = database.PrepareSequenceQuery(
    "d001", key => database.Query().Departments.Where(row => row.DeptNo == key));
var captured = byKey.ExecuteAsync(database, "d001", cancellationToken);
```

These APIs are implemented on the W3 development branch; 0.10 is not released. W3.2-W3.7 remain open, including generated APIs, remaining public families, emitted full-contract/ApiCompat and packed/old/custom consumer evidence. The next implementation slice is lookup and relations.

## W3.2 Lookup And Relation Implementation Checkpoint

Implemented K01/K03-K05 and R01-R11. Database/transaction canonical lookup, the static provider-key helper, public TableCache lookup with its read-only-source fallback, and bounded Memory FindAsync delegate to existing async execution. The new invariant `IAsyncImmutableForeignKey<T>` capability preserves the old synchronous covariance and shared reference state.

`IImmutableRelation<T>` now has one unsupported-by-default async row primitive and the complete accepted collection/terminal/reduction family. Shared defaults are acyclic: row view to values, values to keyed snapshot, keyed snapshot to key access. Built-in relations and the public mock expose virtual concrete methods. Defaults honor overrides and never call synchronous database getters. Built-in async views capture source/key at enumerator construction, load on first move, and honor method/enumerator cancellation during buffered iteration. Dictionary construction rejects duplicate keys consistently instead of silently choosing a row. Typed generated key helpers remain W3.4.

Verification:

- Core and non-friend consumer build for .NET 8/9/10 with zero warnings/errors; consumer execution passes on actual 8.0.31, 9.0.20 and 10.0.12, including concrete/interface relation defaults.
- Six focused custom/default/mock tests pass, covering every numeric overload, extrema/default/cardinality behavior, override dispatch, cancellation, cleanup and explicit unsupported capability.
- Quick run `20260928T152235365Z-455f657f21794a7791d98771bcf8d542`: generators 71/71, unit 3,977/3,977 and SQLite-file compliance 554/554 pass. Memory initially passes 222/223 because its frozen public-surface allowlist expected only Find/Query/Seed. Updated that contract to include the authorized FindAsync declaration and verify its return/token shape; a nullable reflection assertion initially needed correction before compiling. Memory rerun `20260928T152513859Z-6cc2a05ef15d40ae92e26d62f0ec1352` passes 223/223. The failed initial run is retained, not rewritten as green.
- Public relation/lookup provider run `20260928T152535819Z-f8074593fd4849d18b264f915582fd33` passes 12/12 across SQLite file/memory, MySQL 9.7 and MariaDB 12.3. No older server versions were run locally.
- Existing controlled relation coordination and Memory lookup tests now call public entry points. An added controlled enumerator case verifies I/O-free construction and captured keys. Provider cases cover relation membership, warm identity, null/missing lookups, reference covariance, cancellation and post-commit relation source transitions.

Summaries are `artifacts/w3-relations-quick-summary.json`, `artifacts/w3-relations-memory-summary.json` and `artifacts/w3-relations-providers-summary.json`. These are development-working-tree checks with `ValidForEvidence=false`; packed and old-binary acceptance remain open. The initial relation build caught and corrected generic type-argument forwarding for shared Min/Max defaults.

W3.3-W3.7 remain open. The next work is public mutations, transaction callbacks/completion and disposal, with execution-options prerequisites integrated before declaring their configuration contract complete.

## W3.3a Managed Mutation, Lifecycle And Options Checkpoint

Implemented M01-M09 and T01-T04: database-owned mutations, transaction mutation/edit/finite-collection families, model extensions, all four typed database and untyped provider callback forms, managed completion, and async root/transaction disposal. Public methods use the W1/W2 ownership and recovery machinery. Local edits and input capture occur before suspension; caller-owned transactions are not completed by mutations. Callback results wait for finalization and cleanup, including cleanup after pre-callback validation fails. Source-less deletion validates the originating source before invoking the provider callback contract and cannot bypass a poisoned origin.

Added the D06/D07 and C01/C02 prerequisites. Providers capture separate validated execution options before setup, preserve existing constructors/defaults, and offer the fully required options overloads. Transactions capture the effective automatic rollback timeout; mutation helpers, callbacks and disposal use it. It is independent of the request token and does not limit cleanup or unfinished-work draining. Legacy interface options use standard settings. The inherited `IAsyncDisposable` slot has an unsupported default; base providers retain unsupported virtual disposal and built-ins override it. Unsupported managed callback/mutation helpers reject before constructing a legacy provider transaction.

Verification:

- Quick run `20260928T153919930Z-a59c3a57063f43e2956e32aad41857df`: 4,855/4,855 pass (71 generators, 4,004 unit, 223 Memory, 557 SQLite-file compliance), zero failures/skips.
- The existing controlled mutation/edit/completion cases now call the public methods. New lifecycle tests cover all twelve callback receiver/shape combinations, cleanup timing, original callback failure, null tasks, cancellation, validation cleanup and the configured rollback budget. A subsequent focused run adds poisoned-origin source-less deletion and passes 20/20.
- Public provider run `20260928T154131037Z-38a68847bb3841119558b9a30266cdfa`: 12/12 pass across SQLite file/memory, MySQL 9.7 and MariaDB 12.3, including the final source-less provider callback dispatch. No older server versions were run locally.
- Options tests cover null and invalid values before provider setup, inclusive bounds, distinct effective capture, constructor binding and required new parameters.
- The non-friend source consumer builds without warnings/errors and runs on actual .NET 8.0.31/9.0.20/10.0.12. Its legacy provider implements no async members and has throwing synchronous methods; options, callback defaults and inherited async-disposal dispatch behave as specified. This remains source-project evidence, not old-binary or packed-consumer acceptance.
- Initial compilation found a missing namespace import and two internal `token:` argument names in tests moved to public methods; corrected to the public `cancellationToken:` name before successful verification.

Summaries: `artifacts/w3-mutations-quick-summary.json`, `artifacts/w3-mutations-lifecycle-summary.json` and `artifacts/w3-mutations-providers-summary.json`. These are development-working-tree checks, not frozen-candidate receipts.

**Still open:** T05 direct provider-transaction completion requires a standalone lifecycle boundary; exposing managed-only native methods would be incorrect. W3.4-W3.7 also remain open, including generated APIs, lower-level/admin APIs, diagnostic snapshots, emitted manifests and packed/old-binary evidence. This checkpoint does not declare W3.3 or W3 complete.

## W3.3b Direct Provider Transaction Completion Checkpoint

Implemented T05 with unsupported virtual legacy defaults on `DatabaseTransaction` and real SQLite/MySQL/MariaDB overrides. Standalone completion shares native resources and sync/async terminal state; it does not construct a managed wrapper or claim model/cache finalization. Direct async completion/disposal rejects a handle that belongs to a managed transaction. Unused completion does not initialize a connection or transaction.

Standalone commands, readers, completion and cleanup now share an operation gate. Readers retain the slot through native reader and owned-command disposal, including at EOF; rejected competing calls leave the owner intact. The gate supports absent managed transaction IDs instead of inventing ID zero. Completion certainty survives observer/cleanup failures, an uncertain commit cannot be retried, rollback is attempted at most once, and disposal uses the captured independent recovery budget before attempting both cleanup stages.

Verification:

- Quick run `20260928T155212697Z-638b86475a8a474a92ca740d0dd22b61`: 4,867/4,867 pass (71 generators, 4,015 unit, 223 Memory, 558 SQLite-file compliance).
- Final focused run `20260928T155433682Z-707fab7bbcb440d18f4105780f74e3b7`: 52/52 pass after correcting the owned-command cleanup boundary and adding real SQLite commit-denial coverage. An additional focused owned-command-disposal callback case passes 1/1.
- Controlled cases exercise suspended completion/cleanup, cross-sync/async conflicts, original failure identity, ordered independent cleanup failures, no rollback replay and unknown completion after recovery. Real SQLite file/memory authorizer denial verifies native commit failure and subsequent rollback without fabricated certainty.
- Provider run `20260928T155531969Z-4c5113316e374fccb98b59c44ecdb2f1`: 4/4 pass, one standalone lifecycle case each on SQLite file/memory, MySQL 9.7 and MariaDB 12.3.
- External legacy provider-transaction source, with no async overrides and throwing synchronous methods, builds/runs on .NET 8/9/10 and rejects async calls through class and inherited interface dispatch. Builds report zero warnings/errors.

Summaries: `artifacts/w3-provider-completion-quick-summary.json`, `artifacts/w3-provider-completion-focused-summary.json` and `artifacts/w3-provider-completion-providers-summary.json`. These remain development checks; old binaries and packed consumers are separate W3.7 obligations. T01-T05/M01-M09 declarations and their initial public verification are now implemented. W3.4-W3.7 remain open. CI on the preceding managed-lifecycle head `0bc03f89` passed all twelve checks in [run 36445892243](https://github.com/bazer/DataLinq/actions/runs/36445892243); that result is not evidence for later commits.

## W3.6 Diagnostic Snapshot Checkpoint

Completed D01-D05 ahead of the generator/lower-level slices so public callers can inspect the execution evidence already recorded by those paths. D06/D07 and C01/C02 were implemented with the managed lifecycle prerequisite. Added direct-only `DataLinqFailure.GetContext`, `Transaction.FailureContext`, sealed getter-only context/secondary entries and the five independent public enums with the accepted explicit numeric assignments.

Mapping preserves original exceptions and ordered secondary entries, produces defensively protected collections, and exposes no internal scope, SQL/key additions or live execution resources. Public snapshots are cached by internal immutable snapshot identity; later recovery produces a new snapshot while earlier returned values remain unchanged. Internal materialization maps to public RowLoading, finalization to LocalFinalization, and generic internal recovery maps to Rollback only when its operation is known to be rollback; recovery inspection with no such evidence remains Unknown. Unknown internal enum values do not invent public classifications or recovery permissions. The new standalone disposal fallback now identifies an actual rollback failure as Rollback rather than the surrounding Dispose operation.

Verification:

- Core and external source consumer build for .NET 8/9/10 without warnings/errors.
- Three public transaction-diagnostics tests pass: immutable recovery history, overlap isolation and the existing committed-finalization exception contract.
- Three mapping/shape/accessor tests pass in `20260928T160350120Z-e93ffa2e6f9f452b8d3ba38134ba2fd6`, including exact enum values, direct-only lookup, defensive collections and unknown mapping.
- The focused standalone rollback-attribution case passes 1/1.
- Public callback diagnostic assertions pass 4/4 across SQLite file/memory, MySQL 9.7 and MariaDB 12.3 in `20260928T160435338Z-fac1c91a4a344c5e92a33b7198359344`.

Summaries: `artifacts/w3-diagnostics-mapping-summary.json` and `artifacts/w3-diagnostics-providers-summary.json`. These are development-working-tree checks. Full emitted/packed/old-binary evidence remains W3.7. Generator/navigation/mutation emission (W3.4), lower-level/admin APIs (W3.5) and final contract/documentation closeout (W3.7) remain open; no W5 validation behavior was added.

## W3.4a Generated Typed Key Lookup Checkpoint

Implemented K02: generated static GetAsync helpers for IDataSourceAccess, typed Database and typed Transaction. They preserve existing model-side key types, parameter names/order and canonical-key construction, append the optional cancellation token, and return ValueTask with a nullable model result. Converted keys normalize exactly once before entering the shared async lookup. Composite provider-key fast paths remain intact.

Verification:

- All generator tests pass, 79/79, including eight new nullable-enabled/disabled scalar/composite/converted consumer compilation and emitted-symbol cases. Run 20260928T161714722Z-68cd6564c59a461ebbb79d4eb144ccc9.
- Generated public lookup tests pass 8/8 across SQLite file/memory, MySQL 9.7 and MariaDB 12.3. Run 20260928T161803579Z-9d52dd1b04f441299204bf62eb50a54c. Cases cover all three sources, cold/warm keys, converted auto-increment IDs, missing/null keys, cancellation and subsequent transaction reuse.
- Initial provider-test compilation incorrectly passed nullable auto-increment IDs to the existing non-null typed-key signature. Corrected the tests to preserve the accepted synchronous signature. A subsequent test wrongly expected reference identity for a fixture without UseCache; corrected it to verify key equality. Cached employee fixtures still assert identity. These were test assumptions, not product fixes.

Summaries are artifacts/w3-generated-keys-all-generators-summary.json and artifacts/w3-generated-keys-providers-summary.json. Checks remain development-working-tree evidence, not packed/frozen receipts. W3.4 navigation/DLG004 and generated mutations remain open, along with W3.5 and W3.7.

## Standalone Reader Virtual Dispatch Correction

CI on generated-key head b0b40cc9 exposed a regression in the earlier standalone reader change: string-reader execution bypassed the virtual IDbCommand overload. CommandOwnershipTests failed on all four CI compliance targets in run 36450157177. The local navigation quick run 20260928T162658588Z-4897d2732c1d40bcb719934315dbcd27 reproduced it (99 generators, 4,024 unit and 223 Memory cases passed; SQLite compliance was 561/562).

Restored virtual dispatch. Explicit owned-command identity retains the standalone operation through command cleanup, including opening failure and custom reader decorators, without ambient execution permission. Reader and command cleanup each release their share only once. Caller-supplied commands retain their previous ownership.

Verification: all ten focused provider-transaction tests pass in 20260928T163357377Z-e38c22ed539943f09a7fecc525e5cd7f, including four new success/failure/decorator cases. Existing CommandOwnershipTests pass 4/4 across SQLite file/memory, MySQL 9.7 and MariaDB 12.3 in 20260928T163522261Z-a456a9d768d9476bb2ed6e532a2f7366. Summaries are artifacts/w3-reader-dispatch-unit-summary.json and artifacts/w3-reader-dispatch-providers-summary.json. These are development checks; the failed CI result remains part of the record and must be superseded by checks on a corrected head.

## W3.4b Generated Reference Navigation And DLG004 Checkpoint

Implemented R12 on generated public model bases with concrete immutable overrides using the same private reference holder as synchronous navigation. Required references return a non-null ValueTask result or throw an identifying InvalidOperationException; optional references retain nullable results. Async paths never evaluate synchronous navigation. Custom model overrides dispatch normally; legacy models/holders without async support explicitly reject instead of using synchronous getters.

Added error DLG004, Async navigation member conflict, with relation and conflicting-member locations. Semantic probes check token-free, positional-token and named-token calls on base and immutable receivers. Duplicate declarations, unsafe inherited return contracts and partial-class hijacking stop only the affected database's generation. Harmless overloads, compatible inherited virtual methods (including oblivious nullable annotations) and custom derived overrides remain supported. Incremental emission records the override decision and recovers when a conflicting partial declaration is removed. Diagnostic release tracking is updated.

Verification:

- Quick run 20260928T163708440Z-a05b426be93a41ad96bbb4ce109711c3 passes 4,912/4,912: 99 generators, 4,028 unit, 223 Memory and 562 SQLite-file compliance. Zero failures/skips.
- Twenty new generator cases cover emitted return annotations, custom/legacy dispatch, no sync fallback, required/optional behavior, cancellation/original capability exceptions, partial/inherited conflicts, locations/isolation and incremental regeneration. An initial optional-reference fixture with nullable annotations disabled omitted the optional property spelling; corrected the fixture before passing verification.
- Generated navigation provider run 20260928T162515769Z-e020f9348efe41cc964600ea17b94383 passes 8/8 across SQLite file/memory, MySQL 9.7 and MariaDB 12.3, covering cold/warm shared reference state, converted keys, invalidation and committed source transitions.
- One intermediate quick attempt refused a stale prebuilt generator-test graph after core reader changes. A non-incremental generator-test/core build corrected the artifact ordering before the final complete quick pass; the refusal is not counted as product evidence.

Summaries: artifacts/w3-generated-navigation-quick-summary.json and artifacts/w3-generated-navigation-providers-summary.json. These remain development-working-tree checks. Generated mutations remain W3.4 work; a narrowly scoped protected generator bridge has been proposed for explicit API review because the accepted public calls cannot preserve an owned edited mutation's reservation through commit/cleanup. W3.5 and W3.7 also remain open. No W5 runtime-validation APIs were added.

## W3.5a Public Administration Checkpoint

Implemented A01-A04 and A08-A11: database/provider availability probes, metadata import, SQLite journal-mode configuration, SQL factory provisioning and registered SQL/metadata forwarding. Existing internal execution remains responsible for capture, cancellation, cleanup and provider-specific behavior. Legacy interface defaults reject missing async capability without synchronous I/O. Registered provisioning now dispatches the public factory contract, including a custom async implementation with no internal capability; it retains the same captured registration across local SQL generation.

Verification:

- Core and external source consumer build for .NET 8/9/10 without warnings/errors. The consumer runs on actual 8.0.31/9.0.20/10.0.12 and verifies legacy provider/factory defaults with throwing synchronous methods.
- Public SQLite administration: 17/17, run 20260928T164443284Z-54ecde5e466b47a99f741ab4c8a2b621. Public SQLite import/native metadata: 14/14, run 20260928T164726093Z-c2dbc668c9d44064bf429fa1207f8de5.
- Controlled provisioning: 50/50, run 20260928T164633984Z-dd34f7f970d2400088cf7d2140f0fc63. Existing capture, registration, partial-effect, cancellation and ordered-failure cases now exercise public forwarding/factory dispatch, including a new external-style override test.
- MySQL 9.7/MariaDB 12.3 public administration: 15/15, run 20260928T164737271Z-7946b900d6e04c228fb6a91a1f25a276. Metadata: 16/16, run 20260928T164834453Z-1373c5654941429b938663f21eb3819e.
- New root/provider/factory compliance case: 4/4 across the latest four local targets, run 20260928T164845672Z-55d841441f724bb684b904bb9fe792dc. The initial SQLite-file case reached fixture disposal but failed to delete its pooled read-only database handle. Added fixture-owned read-only pool cleanup; no runtime pooling change.

Summaries use artifacts/w3-administration-*, artifacts/w3-public-provisioning-summary.json and artifacts/w3-public-metadata-*. These remain development checks. A05-A07 and their supporting runtime-validation behavior stay in W5. Lower-level L01-L17 and W3.7 remain open.

The user explicitly approved the proposed narrow protected generator helper on Mutable<T> on 2026-09-28. Implement and document that addition with M10; the earlier pending API question is resolved. It must preserve owned edited-mutation input reservations through commit and cleanup rather than relying on a transaction mutation's shorter reservation.

## W3.4c Generated Mutation Checkpoint

Implemented all nineteen M10 InsertAsync/UpdateAsync/SaveAsync receiver shapes. Typed local editing callbacks execute before suspension after input and cancellation validation. Explicit-transaction helpers leave completion to their caller. Database/source-derived helpers own a separate transaction, and immutable Save remains an Update alias.

AAPI-112 records the user-approved protected Mutable<T>.ExecuteGeneratedMutationAsync bridge and its exact signature. An internal generated forwarding method reaches that bridge without adding a public generated support member. The shared owned mutation runner captures after edits and retains reservations until commit and both cleanup attempts finish. Invalid operation kinds are rejected before provider capability checks or transaction creation.

Verification:

- Generated signature/consumer cases pass with nullable annotations enabled and disabled, covering every receiver, named arguments, optional tokens, parameter names, Task<Model> results and internal forwarding accessibility. Focused run 20260928T165936585Z-a1275fe5bb404f6eb22e2e6dba5e3166: 2/2.
- Eight controlled runtime cases pass, including paused commit/transaction cleanup/connection cleanup, conflicting setters/reset/other transaction writes, pre-cancellation and lifecycle validation before edits, editing failure identity/cleanup and invalid protected bridge operation. Run 20260928T170027345Z-04f164c676f5460ab6cec56737d7d2c3.
- Full quick run passes 4,926/4,926 (101 generators, 4,037 unit, 223 Memory, 565 SQLite-file compliance). Run 20260928T170130979Z-2b621bb169304b9fb3d6ccdc408b0642.
- Generated mutation provider cases pass 8/8 across SQLite file/memory, MySQL 9.7 and MariaDB 12.3, covering all nineteen receiver families, persisted results, explicit transaction ownership/rollback, cancellation and immutable-null Save rejection. Run 20260928T170331851Z-5dc76632ae4746c28223a37117d2acd4. An earlier CLI invocation omitted PowerShell quotes around the comma-separated target list and failed target selection before any tests; the quoted invocation is the recorded run.
- CI on the preceding administration commit ce84ff3a passed all twelve checks in run 36454430490. This does not certify the new generated mutation commit.

Summaries: artifacts/w3-generated-mutations-{generator,unit,quick,providers}-summary.json. These are development-working-tree checks, not frozen or packed-consumer receipts. W3.4 implementation is complete; its packed/generated compatibility evidence remains W3.7. Lower-level L01-L17 and the rest of W3.7 remain open; W5 validation and deferred benchmarks are unchanged.
