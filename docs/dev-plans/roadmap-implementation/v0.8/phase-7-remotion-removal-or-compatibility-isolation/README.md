> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 7: Remotion Removal or Compatibility Isolation

**Status:** Planned after Phase 6.

## Purpose

Phase 7 finishes the dependency story. Once the supported path is on the DataLinq parser, Remotion should either leave the main runtime package or move behind a deliberately named compatibility path.

## Preferred Outcome

The preferred 0.8 outcome is:

- `src/DataLinq/DataLinq.csproj` no longer references `Remotion.Linq`
- central package versions no longer include Remotion unless a separate compatibility package owns it
- constrained publish projects no longer root Remotion
- package dependency groups prove Remotion is absent from the main runtime package

## Compatibility Option

A compatibility path is acceptable only if it is explicit:

- clearly named package or mode
- outside the documented practical AOT support boundary
- not required by generated SQLite AOT/trim/WASM smoke paths
- not silently used by ordinary queries that the DataLinq parser supports

Keeping Remotion as an invisible fallback would be the worst of both worlds: the dependency remains, and the new parser's support boundary becomes vague.

## Exit Criteria

- Remotion is removed from the main runtime package, or isolated outside the practical AOT boundary
- package inspection confirms the result
- public docs and release notes describe the exact parser support boundary
- old Remotion-specific tests are removed, moved to compatibility coverage, or rewritten against DataLinq plan/parser concepts
