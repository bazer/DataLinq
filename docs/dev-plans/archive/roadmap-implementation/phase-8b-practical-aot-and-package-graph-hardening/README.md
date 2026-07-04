> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 8B: Generated Contract and Immutable Metadata Foundation

**Status:** Complete for the generated-contract and immutable metadata foundation.

## Purpose

Phase 8 proved a narrow generated SQLite path under Native AOT, trimming, and Blazor WebAssembly AOT. That was real, but the generated runtime contract and metadata graph were not yet clean enough to build the next package/runtime work on top of them.

Phase 8B owns the foundation that had to come first:

- stale generated hooks fail during initialization instead of falling back later
- malformed generated declarations fail early with actionable diagnostics
- metadata producers feed typed drafts into `MetadataDefinitionFactory`
- factory-built runtime metadata snapshots are frozen against ordinary mutation
- public mutable metadata construction APIs are demoted to obsolete compatibility surface

The rest of the practical AOT work is split out:

- Phase 8C owns package graph cleanup, complete generated metadata startup, generated indexed access, and packaging/public wording.
- Phase 17 owns the query-plan, Remotion isolation, supported-subset parser, and SQLitePCLRaw WebAssembly warning disposition work.

## Execution Boundary

In scope:

- generated-hook fail-fast cleanup
- generated declaration validation
- immutable metadata builder/factory foundation
- moving normal metadata production onto typed drafts
- freezing factory-built runtime snapshots against ordinary mutation
- obsoleting mutable compatibility APIs where the product no longer needs them

Out of scope:

- repeatable compatibility size reports
- Roslyn/runtime package graph split
- complete generated metadata startup
- generated indexed value/relation access
- package inspection and public compatibility wording
- replacing or isolating `Remotion.Linq`
- DataLinq query-plan/parser work
- SQLitePCLRaw WebAssembly warning disposition

## Source Plans

- [Implementation Plan](Implementation%20Plan.md)
- [Generated Metadata Contract and Runtime Fallback Removal](../../metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md)
- [Immutable Metadata Definitions and Factory Plan](../../metadata-and-generation/Immutable%20Metadata%20Definitions%20and%20Factory%20Plan.md)
- [Phase 8 Compatibility Results](../phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)
- [Phase 8C Practical AOT Package Graph and Generated Runtime Hardening](../phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/README.md)
- [DataLinq 0.8 Roadmap](../../../roadmap-implementation/v0.8/README.md)

## Exit Criteria

Phase 8B is done when:

- generated output contains `GetDataLinqGeneratedModel()` and does not contain `GetDataLinqGeneratedTableModels()`
- old generated table-model hooks alone are not accepted
- malformed generated declarations fail during provider initialization
- runtime metadata definitions are factory-built immutable snapshots
- ordinary provider/database startup no longer mutates metadata after build
- metadata equivalence tests are green across source, generated, and provider-derived metadata
- cache policy tests prove defaults are not injected by mutating `DatabaseDefinition`
