# W3 Public Async Surface

**Status, 2026-09-28:** implementation started from W2 merge `69a2b2ee40b25862590fa5206901a51aabfbed80`. The user authorized one draft PR from `codex/0.10-w3` to `v0.10`, with incremental commits and pushes collected there. W3 completion, review and merge remain separate checkpoints.

## Scope And Accepted Boundaries

The [API decisions](Async%20Public%20API%20Decisions.md) and [signature inventory](Async%20Signature%20Inventory%20and%20Compatibility%20Matrix.md) remain the contract. W3 exposes the W1/W2 execution machinery, adds public/generated documentation and verifies real consumer compatibility. Existing synchronous APIs remain supported subject to the already approved keyed-enumeration rename and required-reference correction.

**The user explicitly retained runtime schema validation in W5.** Inventory A05-A07, their supporting validation types, comparison/Include/empty-schema policy and runtime-validation evidence remain W5. Hosting and unit-of-work integration remain W4. B16 is a W4/W5 integration obligation, not a reason to implement validation or hosting in W3. W3 owns the existing metadata/probe/provisioning public async families that those later waves will use.

Further performance investigation, optimization and acceptance remain deferred to W8 after feature implementation. Preserve W0/W1/W2 evidence. Continue functional, telemetry and required CI checks. Official SQLite package adoption and affected verification remain tracked follow-ups; W3 does not change package pins or pooling policy by implication.

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

The initial planning commit contains no new public APIs and does not claim any exit gate complete.
