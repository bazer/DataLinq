# Legacy xUnit Removal Plan

> [!WARNING]
> This is an execution plan for removing the retired xUnit test layer. It is not user-facing product documentation.

**Status:** In progress; dependency severance and legacy project deletion completed, sandbox verification partially blocked  
**Goal:** Remove `src/DataLinq.Tests`, `src/DataLinq.MySql.Tests`, and the remaining cutover harness now that the active test architecture is TUnit-based.

## 1. Findings

These findings were captured at the start of the removal work.

The repo is already functionally on the new structure:

- Active suites live in:
  - `src/DataLinq.Generators.Tests`
  - `src/DataLinq.Tests.Unit`
  - `src/DataLinq.Tests.Compliance`
  - `src/DataLinq.Tests.MySql`
  - `src/DataLinq.Testing`
  - `src/DataLinq.Testing.CLI`
- The migration checklist says the remaining work is administrative and operational, not missing test coverage.
- CI already runs the TUnit-based suite set through `DataLinq.Testing.CLI`.

The remaining blockers are legacy wiring, not missing functionality:

- `src/DataLinq.Blazor/DataLinq.Blazor.csproj` references `src/DataLinq.Tests/DataLinq.Tests.csproj` even though the Blazor code only uses `DataLinq.Tests.Models`.
- `src/DataLinq/DataLinq.csproj` still grants `InternalsVisibleTo` to `DataLinq.Tests`.
- `src/DataLinq.Testing.CLI` still exposes `validate-parity`, and CI workflows still run it.
- `src/Directory.Packages.props` still carries xUnit package versions only needed by the legacy projects.
- `src/DataLinq.sln` still includes the legacy projects and a `Legacy Tests` solution folder.
- Contributor docs still describe the xUnit projects as live transition artifacts.

That combination is the worst kind of technical debt: not useful enough to justify itself, but still expensive enough to keep lying to the repo.

## 2. Scope

This removal covers:

- legacy xUnit test projects
- legacy xUnit-only fixture and harness code
- parity validation command and manifest
- solution, CI, package, and documentation references that keep the legacy layer alive

This removal does not cover:

- `src/DataLinq.Tests.Models`
- historical audit documents that are still useful as migration history
- the active TUnit projects and test infrastructure

## 3. Execution Plan

### Phase 1: Sever dependency edges

- Replace `DataLinq.Blazor -> DataLinq.Tests` with a direct reference to `DataLinq.Tests.Models`.
- Remove `InternalsVisibleTo="DataLinq.Tests"`.
- Remove active CLI and CI usage of `validate-parity`.

### Phase 2: Remove the legacy projects

- Delete `src/DataLinq.Tests`.
- Delete `src/DataLinq.MySql.Tests`.
- Remove their entries from `src/DataLinq.sln`.
- Remove the `Legacy Tests` solution folder.
- Remove xUnit package versions that become unused once the legacy projects are gone.

### Phase 3: Clean the documentation

- Update contributor and test-infra docs so they describe only the active suite structure.
- Retain the audit documents as history, but stop describing the parity manifest as a current source of truth.
- Archive the parity checklist as a historical cutover record instead of an active workflow document.

### Phase 4: Verify the cutover

- `dotnet restore src/DataLinq.sln`
- `dotnet build src/DataLinq.sln -c Debug --no-restore`
- `dotnet run --project src/DataLinq.Testing.CLI -c Debug -- run --suite all --alias quick`

That verification bar is not optional. Removing dead infrastructure without proving the active path still works is just a different flavor of sloppiness.

## 4. Done Criteria

The cutover is done when all of the following are true:

- no project references `DataLinq.Tests` or `DataLinq.MySql.Tests`
- no CI workflow runs legacy parity validation
- no solution entry or package management file keeps xUnit alive for removed projects
- contributor docs describe the TUnit suite structure as the only supported structure
- the solution builds and the active quick suite passes

## 5. Progress Notes

- Phase 1 is complete: the Blazor dependency edge was redirected to `DataLinq.Tests.Models`, the legacy `InternalsVisibleTo` grant was removed, and the parity command/workflow wiring was deleted.
- Phase 2 is complete: the two legacy xUnit projects and their tracked source files were removed, the solution was updated, and central xUnit package versions were dropped.
- Phase 3 is partially complete: contributor and infrastructure docs now describe the active structure, and the parity manifest was removed. Historical migration documents were retained and marked as archival context.
- Phase 4 is only partially complete in this environment. Structural checks passed, but full `dotnet` build/test verification is blocked in the sandbox by a broken SDK workload resolver, a blocked roaming `NuGet.Config`, and no network access for uncached packages.
