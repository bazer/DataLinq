> [!WARNING]
> This document is implementation planning material for Roadmap Phase 2. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 2 Implementation Plan: Metadata, Generator, and Diagnostics Hardening

**Status:** Active planning document

## Purpose

This document turns the Phase 2 goals from [Roadmap.md](../../Roadmap.md) into an execution plan that can actually be worked.

The point of this phase is not to chase abstract elegance.

The point is to remove avoidable runtime dynamism from the metadata and instantiation path, make source generation cheaper and more predictable, and stop metadata/generator failures from collapsing into vague nonsense.

Phase 1 gave DataLinq measurement tools.

Phase 2 should use those tools to harden the foundation that later runtime optimization depends on.

## Current Baseline

Several important things are already true:

- Phase 1 now provides a usable benchmark harness, telemetry surface, and baseline/history workflow.
- `RowData` has already moved toward denser indexed storage, so metadata/index work now has a real runtime payoff.
- DataLinq still performs meaningful runtime metadata construction and instance/factory indirection that should be pushed earlier.
- the source generator already knows a lot of structural information that the runtime still re-discovers later.
- generator and metadata failures are still too easy to misdiagnose compared to the actual cost of the underlying mistake.
- the current runtime metadata graph is still setter-heavy and cyclic enough that forcing deep structural equality directly onto it would be invasive and brittle.
- the generator already carries some useful source-span information for properties and default expressions, but the shared failure surface still cannot reliably carry Roslyn locations through the metadata pipeline.
- the generator is incremental in Roslyn hosting terms, but the current pipeline still collects all model declarations into a single graph build, so structural snapshot boundaries still need to be introduced deliberately.

That combination is exactly why this phase should come next.

If metadata shape, equality, and generated factory behavior stay soft, then later query/runtime work will either duplicate effort or optimize on top of unstable seams.

## Phase Objective

By the end of this phase, DataLinq should be able to answer three questions honestly:

1. Is metadata constructed once, deterministically, with structural equality that supports incremental generation and later optimization work?
2. Are object creation and core metadata bootstrapping pushed into generated code where that is practical and defensible?
3. When metadata or generation fails, does the user get a useful, source-located error instead of a vague failure chain?

If we cannot answer all three with a straight face, this phase is not done.

## Design Stance

This phase should not become a giant rewrite disguised as “architecture”.

The right stance is:

- prefer targeted structural refactors that unlock measurable runtime and generator wins
- move compile-time-known work into generated code
- keep runtime metadata and generated metadata coherent instead of maintaining two contradictory worlds
- improve diagnostics as part of the architecture work, not as a cleanup afterthought

The wrong stance would be:

- rewriting every metadata type because records look elegant
- building a perfect builder hierarchy before proving which parts the runtime actually needs
- turning source generation into a second runtime with its own accidental complexity

## Primary Outcomes

This phase should optimize for a small set of outcomes that matter:

- lower startup overhead from metadata and factory bootstrapping
- less runtime reflection/expression machinery on the hot path
- stronger structural equality for metadata snapshots and generator gating
- generator failures that point to real source locations
- a cleaner platform for Phase 3 runtime/query optimization work

## What This Phase Will Deliver

### 1. Stronger metadata construction model

We will define a construction model that separates “building the schema graph” from “using the immutable schema graph at runtime”.

The first target should be a generator-side builder/snapshot model rather than an immediate full rewrite of the live runtime metadata graph.

That is the pragmatic move because it gives Phase 2 the equality and incremental-gating benefits without demanding that every cyclic runtime type become perfectly immutable on day one.

Deliverables:

- explicit builder-style construction where mutability is actually needed
- a structural metadata snapshot shape that can be compared without source-location noise
- runtime definitions that either align with that snapshot directly or can be projected from it without contradictory semantics
- deterministic column/index ordering suitable for dense/indexed access
- structural equality rules that ignore location noise but preserve logical schema identity

### 2. More compile-time bootstrapping

We will stop rediscovering at runtime what the source generator already knows.

Deliverables:

- generated object factory paths that replace avoidable runtime expression/factory work
- generated or generated-assisted metadata bootstrapping where it provides real startup value
- clearer boundaries between generated runtime scaffolding and reflection fallback paths

### 3. Useful diagnostics

We will make metadata and source-generator failures point to useful places.

Deliverables:

- failure objects that can carry source location
- generator/build validation that reports actionable errors
- less generic failure collapse in the metadata/generator path

This should start with a small shared location-carrying failure spine rather than a giant diagnostics framework.

## Workstreams

## Workstream A: Metadata Structure and Equality

### Goals

- separate mutable construction from runtime definition shape
- make metadata equality structural and predictable
- prepare metadata for indexed/runtime work without lying about immutability

### Tasks

1. Audit current metadata types and identify which ones should remain runtime definitions versus construction helpers.
2. Define a builder-to-snapshot flow that can resolve cross-references without circular nullability garbage.
3. Introduce a generator-side structural snapshot boundary before trying to force deep equality onto the entire live runtime graph.
4. Implement structural equality rules for the snapshot or definition shape that actually acts as the generator gatekeeper.
5. Ensure equality ignores source-location-only changes so formatting edits do not trigger pointless regeneration.
6. Lock deterministic ordering for columns and related members where later indexed access depends on it.

### Explicit non-goal

Do not rewrite metadata types just to make them look fashionable. The goal is stronger semantics, not prettier syntax.

## Workstream B: Generated Factories and Metadata Bootstrapping

### Goals

- reduce runtime factory indirection and expression work
- reduce startup metadata reflection where generation can provide the same answer
- make generated runtime scaffolding more obvious and debuggable

### Tasks

1. Audit the current `InstanceFactory` and related runtime construction path.
2. Introduce generated factory hooks or direct generated factory methods for immutable model instantiation.
3. Decide how generated metadata bootstrapping should coexist with existing runtime metadata loading.
4. Prefer a staged migration path:
   - generated factory path first
   - generated metadata bootstrapping next
   - reflection fallback kept only where genuinely required
5. Measure startup/provider-init effects with the Phase 1 benchmark harness after each meaningful slice.

### Explicit non-goal

Do not remove every reflection path blindly. Keep fallback paths until generated replacements are proven.

## Workstream C: Diagnostics and Validation Hardening

### Goals

- make metadata/generator failures source-located and understandable
- validate high-signal mistakes early
- establish one failure-location path that both metadata parsing and generator reporting can use

### Tasks

1. Audit the current failure/diagnostic surface around metadata parsing and source generation.
2. Add source-location carrying failure shapes where generator or metadata build stages can produce actionable errors.
3. Wire the main generator error reporting path to use real source locations when they exist instead of defaulting to generic diagnostics.
4. Improve validation for high-value cases first:
   - invalid or unresolved foreign-key targets
   - invalid default-value/type combinations
   - duplicate or ambiguous model/table definitions where that is currently weak
5. Make generator diagnostics point to the relevant model/property source rather than generic generated output.

### Explicit non-goal

Do not try to design the world’s most general diagnostics framework before fixing the concrete failure cases that are already painful.

## Workstream D: Incremental Generator Behavior

### Goals

- stop unnecessary generator churn
- make structural metadata snapshots usable as generator gatekeepers

### Tasks

1. Map the current generator pipeline and identify where unnecessary recomputation still happens.
2. Introduce an explicit structural snapshot boundary for generator inputs instead of assuming `Collect()` plus Roslyn caching is already good enough.
3. Use structural equality from Workstream A to gate regeneration more intelligently.
4. Verify that source-location changes alone do not force full logical regeneration.
5. Keep the incremental story measurable and observable rather than assuming Roslyn magic did the right thing.

### Why this matters

If schema-equivalent edits still force full generator churn, then a large part of the Phase 2 value is still missing.

## Workstream E: Benchmark and Diagnostics Feedback Loop

### Goals

- use the Phase 1 tooling to verify that this phase is helping
- keep architecture work tethered to measurable effects

### Tasks

1. Define which Phase 1 benchmarks should be watched during Phase 2 work:
   - provider initialization
   - startup first-query
   - warm primary-key fetch
2. Add or refine focused measurement scenarios only where the existing benchmark set is clearly insufficient.
3. Use telemetry and benchmark history to decide whether generated metadata/factory work is moving the right metrics.

## Proposed Execution Order

The order matters.

### Step 1: Audit the current metadata and generator seams

Before writing code, map the actual runtime metadata/factory path and the generator path. The repo should not be “refactored from memory”.

### Step 2: Lock metadata structure and equality rules

Get the builder/snapshot shape and equality semantics right before widening generated output responsibilities.

### Step 3: Improve diagnostics in the same seam

Do this early, not at the end.

If Phase 2 changes metadata and generator structure without improving failure quality at the same time, debugging gets worse precisely when internals are becoming more complex.

### Step 4: Introduce generated factory paths

This is the first likely runtime win and the safer first cut compared to full metadata bootstrapping replacement.

### Step 5: Push more metadata bootstrapping into generated code

Once the structure is stable and diagnostics are not blind, reduce startup reflection and duplicate metadata discovery where generation already has the answer.

### Step 6: Tighten incremental generator gating

Use the new snapshot/equality model to stop unnecessary generator churn.

### Step 7: Re-measure and prune

Use the Phase 1 benchmarks and telemetry to decide what actually improved and what still belongs in Phase 3.

## Exit Criteria

Phase 2 is done when all of the following are true:

- metadata construction and runtime definition shape are clearly separated where mutability is needed
- a structural metadata snapshot exists that supports equality suitable for generator gating
- generated factory paths replace the most important runtime construction indirection
- startup/provider-init work is reduced or at least made more deterministic in a measurable way
- metadata/generator failures can point to useful source locations for the main high-signal failure cases
- Phase 1 benchmark/telemetry tooling can show whether the work helped or regressed startup and hot-path behavior

## Non-Goals

This phase is not allowed to quietly expand into later roadmap work.

Not part of this phase:

- broad SQL generation optimization as the main event
- major query pipeline abstraction work
- async API design
- new providers
- broad migration/schema-validation product work
- speculative memory deduplication work beyond what metadata/index shaping directly requires

## Risks

### Risk: Architecture churn without runtime benefit

If we restructure metadata without reducing startup cost, generator churn, or diagnostics pain, then the work may be technically neat but strategically weak.

Mitigation:

- re-measure after each meaningful slice
- prefer changes that can affect provider-init/startup benchmarks directly

### Risk: Generator/runtime divergence

If generated metadata/factories and runtime fallback behavior drift apart, debugging gets worse instead of better.

Mitigation:

- keep generated and runtime paths aligned through shared snapshot or definition shapes
- introduce generated replacements incrementally instead of swapping everything at once

### Risk: Diagnostics regress while internals improve

It is easy to improve architecture and accidentally make failures more obscure.

Mitigation:

- treat source-located diagnostics as part of the implementation, not documentation garnish

## Review Trigger

This plan should be updated when any of the following happens:

- benchmark evidence shows a different startup/runtime bottleneck than assumed here
- the first generated-factory slice exposes a better or smaller implementation order
- metadata equality or builder design proves more invasive than this phase can absorb cleanly
- enough of the phase is implemented that the document should split into progress tracking and follow-up work
