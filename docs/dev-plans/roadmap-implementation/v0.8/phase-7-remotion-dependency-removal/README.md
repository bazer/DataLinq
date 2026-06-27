> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 7: Remotion Dependency Removal

**Status:** In progress.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

## Purpose

Phase 7 finishes the dependency story. Once the supported path is on the DataLinq parser, Remotion leaves the main DataLinq product dependency graph.

## Preferred Outcome

The required 0.8 outcome is:

- `src/DataLinq/DataLinq.csproj` no longer references `Remotion.Linq`
- `src/Directory.Packages.props` no longer includes a `Remotion.Linq` version for the main product
- constrained publish projects no longer root Remotion
- package dependency groups prove Remotion is absent from the main runtime package
- active unit/compliance tests no longer require Remotion parser APIs
- active runtime source no longer imports Remotion namespaces

## Compatibility Stance

A Remotion-backed compatibility package is not a 0.8 goal. It can be reconsidered later only if a real user need appears and only as a separate package or explicit compatibility mode outside the main runtime dependency graph.

Keeping Remotion as an invisible fallback would be the worst of both worlds: the dependency remains, and the new parser's support boundary becomes vague.

## Exit Criteria

- Remotion is removed from the main runtime package
- package inspection confirms the result
- no active source or test project references Remotion parser APIs except deliberately archived or historical material
- constrained-platform smokes publish without Remotion roots or warnings
- public docs and release notes describe the exact parser support boundary
- old Remotion-specific tests are removed or rewritten against DataLinq plan/parser concepts
