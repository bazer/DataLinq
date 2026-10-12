# W4 DI, Hosting, And Unit Of Work

**Status, 2026-10-12:** implementation started from `3257a184`, after the W3 closeout and the accepted W4 design decisions. The user authorized one draft PR from `codex/0.10-w4` to `v0.10`, collecting coherent implementation/test commits with pushes as needed. Completion, review and merge remain separate checkpoints.

**PR:** [#251, Implement W4: DI, hosting and unit of work](https://github.com/bazer/DataLinq/pull/251).

## Scope And Boundaries

The [integration specification](../../architecture/Dependency%20Injection%20and%20Hosting%20Integration.md), H10-1 through H10-10, and the [W4 integration gates](Implementation%20Order%20and%20Integration%20Plan.md#w4-di-hosting-and-unit-of-work) own the contract. Implement integration in the existing core/provider packages, explicit participant/owner/factory contracts over the existing transaction engine, synchronous rollback intent, optional native results, and generated service composition.

Runtime startup schema validation remains W5. Fake units of work and DI testing replacements remain W6B. Further performance investigation and acceptance remain W8 after feature implementation. No ambient transactions, savepoints, distributed transactions, live configuration, ASP.NET-dependent runtime package, or package publication is introduced.

## Implementation Order

1. **W4.1 — Alias and ownership proof.** Exercise actual Microsoft DI APIs in consumer-shaped TUnit fixtures before dependent registration/generator wiring. Prove reference identity, activation, disposal invocation and underlying cleanup separately for concrete/base/keyed/default services. Cover borrowed instances, lazy ownership transfer, all application-service lifetimes and both shutdown modes. Explicitly record and restrict any unsupported combination rather than relying on container internals or silent ownership changes.
2. **W4.2 — Session and rollback admission.** Add participant/owner contracts and synchronous rollback requests coordinated with existing execution/completion admission. Preserve first reasons, active operations/readers, helper closure, recovery, prepared capture and full mutation return shapes. Prove existing/new consumer binding before freezing session helper overloads.
3. **W4.3 — Owned execution and optional results.** Bind factories to the existing database and transaction helpers. General callbacks allow ordinary/custom/native/no-value results and throw after veto-driven rollback/cleanup; explicitly selected result-aware conveniences preserve the accepted nullable/default semantics and operational failure precedence.
4. **W4.4 — Registration and configuration.** Add lazy per-model/key core and provider registration with singleton read identity, explicit default selection, typed settings, binding/code precedence, optional logging/configuration, owned factories and instance handoff. Carry forward W4.1's proven ownership mechanism and restrictions.
5. **W4.5 — Generated composition.** Add explicit constructor factories, private business bodies, matching sync/async owner and participant wrappers, explicit service lifetimes and keyed/default activation. Preserve session identity, tokens, explicit vetoes, source compatibility and compiler diagnostics.
6. **W4.6 — Integration and closeout.** Verify Generic Host and ASP.NET consumers, concurrent requests, nested participation and shutdown ordering; reconcile public signatures and packed dependency graphs on .NET 8/9/10, retain old consumer/API evidence, update unreleased usage documentation and run the affected broad test/CI gates.

Each slice records actual commands, source identities, results and remaining limits here. Earlier checkpoint evidence is never relabeled as verification of later commits. Preserve stable history and keep one PR description current. Final merge requires separate authorization.

## Opening Audit

- W3 PR #230 established the public async surface and existing managed execution/recovery machinery. W4 extends that machinery rather than implementing a second transaction engine.
- PR #250 settled broad design and explicitly made alias disposal, generated-helper source compatibility and participant prepared execution implementation prerequisites.
- `Database<T>` implements disposal and owns its provider. Ordinary Microsoft DI factory aliases can capture the same disposable instance more than once; a non-disposable interface alone cannot prevent capture. This must be resolved before wiring registrations.
- Core currently references logging abstractions. Add only Microsoft.Extensions dependencies actually required by implemented APIs, then verify restored and packed graphs; no new DataLinq package is planned.

## Evidence

### W4.1 Initial Container Proof

Consumer-shaped TUnit fixtures use `Microsoft.Extensions.DependencyInjection` **10.0.11**, added only to the unit test project. The production dependency graph is unchanged. `DependencyInjectionOwnershipProofTests` covers all 24 resolution orders of concrete/interface and keyed/default identities, repeated resolution, independent scopes, all three lifetimes and sync/async shutdown. `TransactionMutationFailureTests.DiOwnership` exercises actual `Database<T>` dispatch over the existing controlled root cleanup machinery, including the original cleanup exception and public failure snapshot.

Observed descriptor behavior:

| Pattern | Evidence and disposition |
| --- | --- |
| Borrowed existing instance with instance descriptors for every disposable alias | All aliases share identity and receive zero container disposal calls. A factory returning that borrowed object is unsafe, even through a non-disposable interface. |
| Lazy owned singleton with factory aliases | One activation, four disposal invocations when all four descriptors resolve. The existing owned-root guard performs one underlying cleanup attempt. Shutdown stops at a throwing disposal and preserves the original failure. This alone is not proof for arbitrary custom provider cleanup. |
| Explicit transfer through a canonical factory | No activation at container construction or never-resolved shutdown; a throwing activation is not captured. Successful aliases share the activation. Production wiring must place all activation validation before publishing the disposable object. |
| One service identifier, singleton/scoped/transient | Correct lifetime and one appropriate disposal invocation per activation. Sync-only services use sync cleanup during either shutdown mode. Async-only services require async shutdown; sync shutdown throws without inventing a sync fallback. |
| Non-disposable singleton/scoped keyed/default aliases | Shared identity within the expected lifetime, without disposal capture. |
| Disposable transient keyed/default entry points | Independent factories invoke the same recipe directly, creating distinct activations with one disposal each. Forwarding one captured disposable transient through another factory is not this pattern. |
| Disposable singleton/scoped keyed/default aliases | Ordinary factories duplicate cleanup calls. The implementation restriction below is required before dependent generated registration wiring. |

**W4 implementation restriction:** generated application services implementing `IDisposable` or `IAsyncDisposable` may use a single unnamed registration or a keyed registration without a default alias for singleton/scoped lifetimes. Reject keyed-plus-default aliasing for these disposable lifetimes with a clear registration diagnostic, independent of fluent call order. Non-disposable singleton/scoped services retain alias identity; disposable transients use direct independent activation recipes. This explicitly narrows H10-7 under its authorized proof-driven restriction rule; it does not silently assume application disposal is idempotent or modify authored disposal members. The diagnostic and generated consumer evidence remain required before exposing production registration.

Verification on the development working tree:

- Release unit project build: zero warnings/errors.
- Container proof: **14/14** cases passed, run `20261012T112831021Z-d195ed5f37b440538a85e2c552a21c14`.
- Controlled DataLinq root proof: **6/6** cases passed, run `20261012T112847043Z-5ab0e05d3ccd4b0e9f6cf7d74f8e8ba4`.
- An initial fixture build failed because `ServiceCollection.Add` is explicitly implemented; the fixtures now use `IServiceCollection` for direct descriptor insertion. No failed run is counted as evidence.

Commands use the sandbox wrapper and Testing CLI `run --suite unit --filter` with `/*/*/DependencyInjectionOwnershipProofTests/*` and `/*/*/TransactionMutationFailureTests/DiOwnership*`.

**Still open:** actual production registration/generator preservation, custom-provider once-only cleanup, generated read roots with user-added disposal interfaces, .NET 8/9 consumer execution, complete instance handoff failure ordering, dependent operation shutdown ordering and all W4.2-W4.6 work. These initial fixtures are a prerequisite checkpoint, not W4 completion. Session/rollback implementation can proceed independently of registration wiring.

### W4.2 Rollback Admission And Optional Result Values

`Transaction.RequestRollback` now records an irreversible business veto under the existing operation gate. It preserves the first diagnostic reason, accepts requests while an admitted command or reader is active, and rejects fresh execution/commit after acceptance. Already admitted work retains its continuation rights. Completion admission, terminal state, disposal and helper closure reject late requests. No provider I/O occurs just to request rollback.

Optional typed `TransactionResult`, `Success`, `Rollback` and `Unit` values are independent of that control state. Nullable/default success payloads are valid; uninitialized result inspection, wrong-case access and null rollback reasons throw. A returned failure value or a caught inspection exception does not itself prevent commit. Existing general transaction callbacks report an explicit veto by throwing after recovery/cleanup, including when its marker is ignored.

Controlled tests cover request-versus-completion admission, pending mutations and reader moves, open/between-move readers, unused deferred enumerators, concurrent/repeated reasons, escaped-reader draining, helper closure, and rollback failures. Provider tests verify persistence outcomes for manual and callback ownership in both execution modes, failure data without a veto, and continued reader execution after a veto.

Verification before this slice's commit:

- Release core build on **net8.0/net9.0/net10.0**: zero warnings/errors.
- Release quick plan: **5,009/5,009** passed (101 generator, 4,115 unit, 223 Memory, 570 SQLite compliance), no skips; run `20261012T121816323Z-a13ef4ce9d6940668e370e57935b1bb3`.
- Focused `RollbackRequestTests` on **all eight provider targets**: **16/16** passed, no skips; run `20261012T123340963Z-c75b8856b4a9464fb2afd97caead1ea6`. Includes SQLite file/memory, MySQL 8.4/9.7, MariaDB 10.11/11.4/11.8/12.3.

Commands for the quick run use `scripts/dotnet-sandbox.ps1` with the built `src/DataLinq.Testing.CLI/bin/Release/net10.0/DataLinq.Testing.CLI.dll`, followed by `run --plan quick --configuration Release --output failures`. Passing the DLL directly preserves the CLI's configuration argument through PowerShell. The provider run uses the same DLL with `run --suite compliance --configuration Release --alias all --filter '/*/*/RollbackRequestTests/*' --output failures` and `DATALINQ_TEST_DB_HOST=127.0.0.1`.

The initial provider attempts could not reach Podman's socket. After the user restarted the containers, all six servers and their loopback ports were confirmed live. The redirected sandbox profile still failed to reach Podman; the successful provider run used ordinary `dotnet` outside the sandbox with the normal Windows profile. These infrastructure failures are not counted as passing provider evidence, and no product or container configuration was changed to conceal them.

**Still open:** participant/owner/factory interfaces, prepared participation and generated helper source compatibility, result-aware owning helpers and generic conversion consumer edge cases, and the W4.1/W4.4-W4.6 ownership/registration/generation/integration gates. This is a rollback-control checkpoint, not completion of W4.2 or W4.3.

### W4.2 Participant And Owner Surface

`Transaction<TDatabase>` directly implements `IDataLinqUnitOfWork<TDatabase>`, inheriting the independent `IDataLinqSession<TDatabase>` contract. The session exposes the full existing read/mutation families, actual immutable mutation results, nullable Save-with-changes behavior, diagnostics and rollback requests. Completion and disposal are owner members. The interface inventory does not inherit or expose `IDataSourceAccess`, raw provider access or physical completion. Synchronous Save annotations now accurately describe the existing null-means-new behavior.

Prepared participation uses `session.Execute(prepared, argument, token)` / `ExecuteAsync`, with scalar/row and sequence overloads. These forward through the existing prepared invocation, validation and managed execution machinery using the same transaction. Existing source-taking prepared overloads are unchanged. Provider tests prove uncommitted writes are visible through the selected execution transaction when preparation used a different database instance, and that sequences capture arguments before enumeration. Controlled tests additionally cover scalar capture/cleanup and prepared reader overlap, cancellation, escaped work and helper draining through participant-typed callers.

Generated Insert/Update/Save helpers now include participant overloads and concrete `Transaction<TDatabase>` overloads to avoid ambiguity with the existing non-generic `Transaction` overloads. Old declarations and parameter names remain; immutable generated Save retains its Update behavior. These helpers reuse the supplied owner and do not create or complete another transaction. Source-less owning helpers are unchanged.

Verification before this slice's commit:

- Release quick plan: **5,021/5,021**, no skips (107 generator, 4,118 unit, 223 Memory, 573 SQLite compliance); run `20261012T124816105Z-b825dc0d770c473792eebaab722bd3f2`.
- Focused `SessionTests`: **24/24** across all eight provider targets, no skips; run `20261012T125331412Z-223d251398c848fa9c2987d4ed80e20f`, using the normal Windows profile for Podman as above.
- After consolidating consumer binding sources, focused generator compilation: **6/6**, C# 12/13/14 with nullable analysis on/off; run `20261012T125445261Z-7b5823b7bcac4d8da213f89ab5105c31`. Base/typed/inferred/participant variables, generic constraints, model/transaction receivers, named/static/method-group calls and optional/explicit tokens compile from shared fixture sources.
- [`W4SessionConsumer`](../../../../tests/fixtures/W4SessionConsumer/README.md) builds on net8.0/net9.0/net10.0 with C# 12 and warnings treated as errors, and executes successfully on **.NET 8.0.31, 9.0.20 and 10.0.12**. It links unchanged W3 mutation binding sources in addition to the new shared bindings. The .NET 9 host is the existing workspace-local runtime; the system host lacks .NET 9 and was not rolled forward to simulate it.

The first broad attempt hit a Windows file lock when the Testing CLI tried rebuilding its own dependencies through the unit project. Building the unit project before starting the CLI resolved it. A first generator fixture omitted a namespace import; that compile failure was fixed before the passing runs. The final production source was unchanged between the quick/provider runs and the focused fixture consolidation verification.

**Still open:** factory-owned sync/async execution and explicit result-aware conveniences; generated service composition with another standalone registration and all relevant prepared capability/compatibility edge cases; packed-consumer/API reconciliation; ownership, registrations, options and host shutdown gates from W4.1/W4.4-W4.6. The source-project consumer is not packed-artifact evidence, and W4 remains incomplete.

### Optional Result API Refinement

The draft result's success accessor is now `SuccessValue`, keeping `Value` and `HasValue` unclaimed as required by the accepted future-union boundary. No native union integration is enabled. `FromSuccess` and `FromRollback` provide explicit construction when `T` itself is `Rollback<TFailure>`: the two implicit conversions necessarily overlap for that unusual closed generic type. `TransactionResult.Success(marker)` also constructs marker-shaped success data explicitly. These constructors remain data-only and never request rollback.

The expanded result fixture passes **4/4** focused cases (run `20261012T130308022Z-9ca6a16b632c4084893936a45cff2b0d`); the updated non-friend consumer builds without warnings and passes on .NET 8.0.31/9.0.20/10.0.12, including explicit marker-shaped construction. This small API follow-up does not rerun or relabel the earlier broad/provider evidence. Before it, all twelve CI checks at `e77eaf20` completed successfully, including the latest required gate, local suites, both latest server families and dependency audit ([run 38252345771](https://github.com/bazer/DataLinq/actions/runs/38252345771)).
