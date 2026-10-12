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

### W4.3 General Factory-Owned Execution

`IDataLinqUnitOfWorkFactory<TDatabase>` and `DataLinqUnitOfWorkFactory<TDatabase>` bind to an existing database without transferring its ownership. `Create` returns the existing lazy logical transaction for caller-controlled completion/disposal. General `Execute` / `ExecuteAsync` callbacks receive participant sessions and accept ordinary/custom/native/no-value payloads. A failure-shaped return alone can commit; an explicit veto instead throws after recovery and cleanup, preserving the original reason.

The async path delegates to the existing managed database callback engine. The synchronous path shares the existing operation gate, synchronous commit/finalization and synchronous disposal. It closes helper admission and drains escaped admitted work before cleanup; only this exceptional escaped-async-work case can require waiting for work already in flight. It does not schedule callbacks on a worker or replace ordinary synchronous provider calls with async calls. Casting a participant back to the concrete owner cannot bypass callback completion/disposal guards.

Verification before this slice's commit:

- Focused controlled factory cases: **10/10**, including all completion/disposal bypass attempts, cancellation, cleanup failure precedence, escaped synchronous readers and pending async work; run `20261012T131411606Z-0e06b5696a81496fabed1b3ca43e8bec`.
- Release quick plan: **5,033/5,033**, no skips (107 generator, 4,129 unit, 223 Memory, 574 SQLite compliance); run `20261012T131425562Z-6b708d1924874104baeed13c328b7788`.
- Focused factory compliance across **all eight targets**: **8/8**, no skips; run `20261012T133058214Z-c83e5870fb8b41909b80c3c5300a5c26`, using `run --suite compliance --configuration Release --alias all --filter '/*/*/UnitOfWorkFactoryTests/*'` with the previously documented normal-profile Podman invocation.
- Updated non-friend factory consumer: zero-warning Release build and successful execution on **.NET 8.0.31, 9.0.20 and 10.0.12**.

The initial synchronous helper build lacked its transaction-status namespace import; the passing runs follow that correction. These are development-tree checks, not clean packed-artifact evidence. Explicit result-aware conveniences are the next W4.3 step; production DI, configuration, generated services, ownership and host/packed/API closeout gates remain open.

### W4.3 Explicit Result-Aware Convenience

`ExecuteResult` / `ExecuteResultAsync` are opt-in factory extensions for string and typed rollback payloads, with optional explicit async token delivery. They inspect results inside the owned callback, before commit; invalid defaults throw without manufacturing a veto or poisoning the session. A failure payload without a veto can still commit. A veto paired with success data still throws. Mapped rollback payloads preserve the session's first diagnostic reason.

Only the owning completion boundary can confirm a fresh veto as returnable, after confirmed rollback (or initialization proven unused under closed admission) and successful finalization/cleanup. The confirmation is tied to the transaction ID and contains no live resource reference. `NotAttempted` alone is insufficient; failed initialization, poisoned transactions, reused callback-thrown vetoes, secondary failures and cleanup failures cannot become normal typed outcomes. General callback behavior remains unchanged.

Verification before this slice's commit:

- Controlled result-helper cases: **22/22**, run `20261012T134125913Z-af4fa30a9d4b407e8200787f4d621117`.
- Release quick plan: **5,056/5,056**, no skips (107 generator, 4,151 unit, 223 Memory, 575 SQLite compliance); run `20261012T134439711Z-5d2b4d9a552f4d1ea6b5374d6d208e9b`.
- Result-aware compliance: **8/8** across all provider targets, run `20261012T134459341Z-eee605d8a8654825bacff3039e73761b`. Each target exercises sync/async execution, both unused and writing transactions, and payloads with/without explicit vetoes.
- Public consumer: zero-warning build and successful execution on .NET 8.0.31/9.0.20/10.0.12, including typed/string rollback and native no-value success.

The first test compile used a read-only fixture property and an ambiguous test-namespace `Unit` name; both were corrected before the passing runs. The first broad run hit the existing Windows Testing CLI self-lock. After building the generator, unit, Memory and compliance projects outside the running CLI, the successful quick/provider runs used `--no-build`; no failure is counted as passing evidence. All twelve CI checks for the preceding factory commit `7317a767` are green ([run 38256588307](https://github.com/bazer/DataLinq/actions/runs/38256588307)); they do not verify this later working tree.

**Still open:** production ownership and lazy registration/configuration, generated service composition/result opt-in, complete host/shutdown, packed/API and documentation closeout. Result-helper source verification is not W4 completion.

### W4.1 Custom Provider Ownership Mechanism

Before wiring registration, the actual-container proof now covers a non-idempotent custom provider through concrete/base/keyed/default/read aliases and multiple database wrappers. An internal container-ownership guard is enabled only after activation validation. It shares one provider cleanup attempt across database wrappers, preserves original cleanup failures, and leaves ordinary manually owned database behavior unchanged. Async capability is determined from the actual disposal delegate target, without invoking cleanup or scanning members. Legacy async disposal rejects before closing the guard, so later explicit synchronous disposal remains available.

Database subclasses that replace the `IDisposable`/`IAsyncDisposable` dispatch cannot use owned aliases: they could bypass the guard or duplicate arbitrary authored cleanup. Reject that owned combination before handoff. Such instances remain supported as borrowed instance descriptors, which receive no container disposal invocation. Do not rewrite authored disposal.

The Release core builds on net8.0/net9.0/net10.0 without warnings. **11/11** focused container cases passed on the working tree, run `20261012T135443733Z-0ae09c02203f4b9b997e77ce3bb7c292`. One initial test referenced the non-generic read-access surface; its corrected typed access precedes the passing run. Production wiring, disposable generated read roots, all activation entry points, and external runtime consumer evidence remain required before closing the ownership gate.

### W4.4 Lazy Core Registration And Ownership Handoff

`AddDataLinq<TDatabase>` defines an unnamed or keyed identity. `AsDefault` explicitly exposes a keyed identity through unnamed injection. `UseFactory` is lazy and container-owned; `UseInstance` borrows by default and accepts explicit container ownership. Concrete/base database, read-access/model, and factory aliases resolve the same activation. Existing instances expose their actual concrete type, including when supplied through a base variable; factories expose their declared result type. Duplicate identities, conflicting defaults, competing sources, and known borrowed/owned provider conflicts fail without constructing or disposing a provider.

Each container owns an activation holder. Resolving only a non-disposable alias still transfers successful owned activation to that holder; never-selected and failed existing-instance activations retain caller ownership. Borrowed disposable read objects use instance descriptors and receive zero container disposal calls. Arbitrary disposal on owned read objects and database subclasses that replace disposal dispatch are rejected before handoff. A custom non-idempotent provider receives one underlying cleanup attempt across owned aliases. Construction and shutdown are serialized, failed lazy activations do not acquire ownership, and mutation after activation is rejected.

Verification on the final development tree:

- Focused registration cases: **22/22**, run `20261012T142112482Z-ad6bad63bda74fa6854de8c806e714dd`. Cases iterate all twelve keyed/default entry points, both builder orderings, borrowed/transferred/factory ownership and synchronous/asynchronous shutdown, plus failure and conflict paths.
- Release quick plan: **5,089/5,089**, no failures/skips (107 generator, 4,184 unit, 223 Memory, 575 SQLite compliance); run `20261012T142250469Z-7931b5a719d34d83871e2b2fbb2b2357`.
- Public non-friend consumer: zero-warning Release build and successful execution on actual **.NET 8.0.31, 9.0.20 and 10.0.12**. It proves lazy keyed/default identity, factory execution, custom-provider once-only cleanup and borrowed disposable generated read-model behavior.

All affected suite projects were freshly built before the final `--no-build` quick run. An earlier incremental build left an old entry-assembly timestamp and the CLI correctly refused stale execution; forced rebuilds supplied the passing final evidence. The public fixture initially lacked its generator-required namespace, corrected before its successful build. All twelve CI checks for `ce816edb` passed ([run 38258331369](https://github.com/bazer/DataLinq/actions/runs/38258331369)); those checks precede this registration commit.

Provider options/configuration/logging, generated service authoring and registration, dependent host shutdown, and packed/API/documentation closeout remain open. The tests above establish core registration ownership, not completion of W4.

### W4.4 Provider Configuration And Logging

`UseSQLite`, `UseMySql` and `UseMariaDB` explicitly select provider construction; their literal and `ConnectionString`-suffixed named forms remain separate. Typed provider builders expose `Bind`, `Configure`, `AsDefault` and the common `Registration`. Options support `ConnectionString`, `ConnectionStringName`, `DatabaseName` and `Execution`. Bindings run in order before all code callbacks, regardless of fluent call order. Code connection selection replaces both bound alternatives as a whole. An ambiguous or missing effective source fails; a named source requires the application's `IConfiguration`, while a literal requires none.

Each container activation creates fresh options, resolves and validates its connection string, captures immutable execution settings and then constructs the provider with an optional host `ILoggerFactory`. Configuration reload or retained-option mutation cannot change an activated database. Driver parse failures discard potentially sensitive messages and inner exceptions. Invalid settings fail before SQLite file/setup work or MariaDB's existing synchronous version probe. Existing constructor signatures remain; additive four-argument database constructors forward execution options to the existing provider constructors. Core adds only `Configuration.Abstractions`; concrete configuration and DI implementations are consumer/test dependencies. No live options monitor, provider selection through configuration, startup validation or new hosting package is introduced.

Verification before this slice's commit:

- Focused configuration tests: **18/18**, run `20261012T143817272Z-686a79127c5a4582af3c65b7689663ec`. Includes precedence, per-key/container isolation, optional logging, named/literal replacement, retained options, safe diagnostics, pre-setup validation and never-resolved SQLite file creation checks.
- Provider registration compliance: **8/8** across all eight targets, run `20261012T144104083Z-b7d37a75661d4a819b1b2cb2e63b58fb`. Exercises actual selected providers, aliases, immutable settings, sync/async owned commit/veto paths, and independent provider survival after container shutdown.
- Release quick plan: **5,108/5,108**, no failures/skips (107 generator, 4,202 unit, 223 Memory, 576 SQLite compliance); run `20261012T144236709Z-ba8b936811a64f79bf456fd005ec53c3`.
- Provider libraries and public consumer build without warnings/errors on net8.0/net9.0/net10.0; the expanded public consumer executes successfully on actual **.NET 8.0.31, 9.0.20 and 10.0.12**.

The initial build lacked restored assets for the new abstraction reference; explicit sandboxed restore succeeded and all recorded builds/runs follow it. The quick run used prebuilt suites with `--no-build`; all-target verification used the previously documented normal-profile Podman invocation. All twelve CI checks at the preceding core registration commit `f399f916` passed ([run 38263533626](https://github.com/bazer/DataLinq/actions/runs/38263533626)); this does not relabel them as checks of the provider-options commit.

Generated service authoring/registration and its ownership restrictions, dependent host/shutdown tests, remaining composition/capability checks, packed/API compatibility, and usage/documentation closeout remain open.

### W4.5 Generated Service Entry Points

`IDataLinqService<TDatabase>` marks a concrete top-level partial service. A primary constructor supplies its matching factory by type; a traditional constructor uses one `[DataLinqFactory]` instance field or readable property. `[DataLinqOperation]` on a private `NameCore` / `NameCoreAsync` method generates public owning and session-taking `Name` / `NameAsync` methods. Supported return families are `void`, ordinary/custom/native values, `Task` and `Task<T>`. Async bodies take a final explicit cancellation token; generated entry points make that token optional and forward it. `[DataLinqOperation(ResultAware = true)]` explicitly selects the existing native-result helper; declaring a native result alone does not select that behavior.

Owning methods call shared factory helpers. Participants call the new `IDataLinqSession.ValidateParticipation` admission check and invoke business code on the supplied session without completing or disposing it. The check reuses transaction lifecycle/integrity/admission validation without constructing another adapter or dispatching a command. A generated static `IDataLinqServiceFactory<TDatabase,TService>` bridge binds one selected factory and resolves ordinary required/optional or explicitly keyed constructor dependencies, without reflection-based constructor selection or hidden session initialization. Direct service construction remains usable.

The current bounded declaration inventory supports non-generic concrete top-level classes and non-generic private instance operations. Ref/out/in, pointer/ref-like signatures, async void, ValueTask/custom awaitables, operation-parameter attributes and generated-name collisions are rejected with `DLG005`. Traditional factory designation, constructor ambiguity, mismatched sessions, naming conventions, final async tokens and incompatible result-aware opt-in are also diagnosed. Application-authored disposal is not rewritten. Service registration/lifetime wiring is the next layer.

Verification before this slice's commit:

- Generator cases: **20/20**, run `20261012T145723387Z-0d212bf3aaf94600b103d397dd16ced4`. Six shared consumer compilations cover C# 12/13/14 with nullable analysis on/off without warnings/errors; fourteen invalid-declaration cases verify actionable diagnostics without generator crashes.
- Controlled participation guard: **2/2**, run `20261012T150359555Z-b795771998e544f18f23c5a05ca78a13`, including active readers, recovery of admission after reader disposal and helper closure before wrapper disposal.
- Release quick plan: **5,130/5,130**, no failures/skips (127 generator, 4,204 unit, 223 Memory, 576 SQLite compliance); run `20261012T150413131Z-df136d8206ea4483af051b6c775662b9`.
- Public consumer builds without warnings/errors and executes on actual **.NET 8.0.31, 9.0.20 and 10.0.12**. It covers both constructor styles, custom/native/no-value results, tokens, standalone writes, participation through a differently bound service, prepared query identity, child/parent veto mapping, invalid local results and sixteen concurrent owning calls with distinct sessions.

The first generator fixture lacked its DI abstraction metadata reference, and the first executable fixture lacked the provisioning namespace import; both were corrected before the passing runs. A guard assertion initially confused logical adapter construction with provider I/O; its corrected before/after adapter and command counts precede the final passing quick run. The earlier failed quick run is not passing evidence. All twelve CI checks at the preceding provider registration commit `90f32bc9` passed ([run 38265211508](https://github.com/bazer/DataLinq/actions/runs/38265211508)); they do not verify these later generator changes.

Production singleton/scoped/transient service registration and its disposal restrictions, host/shutdown consumers, full composition/capability and packed/API/documentation closeout remain open.

### W4.5 Generated Service Registration And Provider Composition

Both common and provider builders now expose `AddSingletonService<TService>`, `AddScopedService<TService>` and `AddTransientService<TService>`. Their generated construction bridge receives the selected registration's factory; ordinary constructor dependencies remain ordinary DI dependencies unless explicitly keyed. Non-disposable singleton/scoped keyed/default aliases share one instance in the requested lifetime. Transients use direct independent recipes for each lookup, avoiding duplicate disposable capture through forwarding aliases.

The established restriction is now enforced before activation: disposable singleton/scoped application services cannot have both a key and a default alias. Both fluent orders reject that combination. One unnamed identity or one keyed identity remains supported. Authored sync/async disposal is preserved, counted once per supported activation, and cleanup exceptions propagate without replay. Sync shutdown of an async-only service retains the container's explicit unsupported behavior. Default selection and duplicate-service conflicts fail structurally, without provider construction; standard captive-scope validation remains effective.

Application-service construction first activates the selected DataLinq infrastructure/factory. After that handoff succeeds, a later missing application dependency does not return a transferred database to caller ownership. Never-selected registrations still transfer nothing, and borrowed instances remain borrowed through either failure path.

Verification before this slice's commit:

- Focused service registration: **24/24**, run `20261012T155116282Z-bd8c6fc9c89b4a449e79b57a6d5460a8`. Cases iterate all lifetimes, unnamed/keyed/default identities, both builder/resolution orders and sync/async disposal interfaces, including cleanup failure, unsupported shutdown, scope validation and service-first instance handoff.
- Generated nested composition: **8/8** across all provider targets, run `20261012T154646665Z-c382e11269ed4555a26755d99effccc8`. A child's standalone method writes to its own database; its participating method sees the parent's transaction-local writes through a plan prepared on the other registration. Sync/async parent helpers preserve the child's first veto reason while returning a mapped result only after owned recovery/cleanup.
- Release quick plan: **5,155/5,155**, no failures/skips (127 generator, 4,228 unit, 223 Memory, 577 SQLite compliance); run `20261012T155122169Z-24d559898c884f20b9a0f943a6a92ed9`.
- Expanded public consumer: zero-warning builds and successful execution on actual **.NET 8.0.31, 9.0.20 and 10.0.12**, including provider-builder service wiring and disposable transient cleanup.

All twelve CI checks for the preceding generator commit `3e094948` passed ([run 38267840848](https://github.com/bazer/DataLinq/actions/runs/38267840848)). Host/request/shutdown integration, final API/capability reconciliation, packed/old-binary consumer evidence and usage/documentation closeout remain open; W4 is not yet complete.
