# W4 DI, Hosting, And Unit Of Work

**Status, 2026-10-12:** implementation started from `3257a184`, after the W3 closeout and the accepted W4 design decisions. The user authorized one draft PR from `codex/0.10-w4` to `v0.10`, collecting coherent implementation/test commits with pushes as needed. Completion, review and merge remain separate checkpoints.

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

The opening plan contains no implementation or completed exit-gate claim. W4.1 is the first verification target.
