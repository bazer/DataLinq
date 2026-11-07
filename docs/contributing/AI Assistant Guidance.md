# AI Assistant Guidance

This page describes the development loop for AI assistants and coding agents working in DataLinq.

## Instruction Ownership

- `AGENTS.md` owns repository rules and authorization boundaries.
- [Contributing](../Contributing.md) owns shared contributor conventions, commit-message style, and local test selection.
- This page owns the agent workflow from inspection through handoff.
- [Internal Tooling](Internal%20Tooling.md) routes build, test, benchmark, and release-evidence work to the appropriate tools and owns platform troubleshooting.
- Per-tool pages document command behavior. Read the pages relevant to the task instead of loading every tool reference.

Keep rules in their owning page and link to them. Thin compatibility entry points such as `.github/copilot-instructions.md` may direct clients to the root `AGENTS.md`; they must not duplicate policy. Do not use product-specific instruction files or planning documents as competing operating guides.

## Development Loop

### 1. Inspect and scope

Read `AGENTS.md`, inspect the current branch and both staged and unstaged changes, and identify the code, tests, and docs affected by the request. Preserve unrelated work, including existing index state.

For a clear, bounded request, proceed with implementation. Broad documentation, cleanup, or architectural work starts with an audit and action plan. Once scope is agreed, work autonomously until the slice is ready to review. Ask again only for a material decision or missing information that blocks the task.

### 2. Implement and iterate

Add or update regression coverage for behavior changes in the active TUnit projects. Use existing shared harnesses and fixtures where appropriate.

Run only the tests needed to exercise the behavior being edited: a method, class, or small relevant group. Use `DataLinq.Testing.CLI run --plan focused` with an explicit suite and TUnit tree-node filter. The shape is `/<Assembly>/<Namespace>/<Class>/<Test>`; do not use VSTest expressions such as `FullyQualifiedName~Foo`.

The examples on this page run from the repository root:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- run --plan focused --suite unit --filter "/*/*/CacheNotificationManagerTests/*" --output failures
```

Select a specific provider target when the regression needs a server database. The `memory` suite is targetless and distinct from the `sqlite-memory` compliance target; see [Testing CLI suites](DataLinq.Testing.CLI.md#suites).

Restore or diagnose the environment when needed, and build only the affected projects during iteration. A full solution build, repeated `doctor` runs, smoke runs, and provider matrices are not mandatory steps after every edit. Use the [contributor feedback loop](../Contributing.md#local-feedback-loop) to choose the next check.

### 3. Verify the completed slice

Apply the [contributor feedback loop](../Contributing.md#local-feedback-loop) to choose the stopping-point checks for the completed slice. That section owns local test selection, rerun criteria, and documentation-only validation. Its command examples run from `src`; when running them from the repository root, prefix project paths with `src/`.

Report exactly what was verified and any unavailable or failing checks. A local passing run does not replace the CI gate or release evidence; see [CI Test Lanes](CI%20Test%20Lanes.md).

### 4. Review, stage, and hand off

Review the final diff for correctness, unintended changes, missing coverage, and documentation impact. Check whitespace and inspect the staged diff after staging the task's changes. If a file also contains unrelated work, stage only the relevant hunks; do not sweep unrelated files or existing staged edits into the task.

Before requesting approval, refresh the relevant remote refs and inspect the entire outgoing commit range, including older local commits. For an existing remote branch, compare against its current tip; for a new remote branch, compare against the intended PR base. Inspect the commit list as well as the resulting diff, since a scoped staged change does not guarantee a scoped push. If the range includes unrelated or unapproved commits, isolate the approved work on a suitable branch or obtain approval for the expanded scope before publishing it. Recheck before pushing if either the local history or remote destination has changed.

Summarize what changed, the checks and outcomes, and any remaining limitations. At a sensible commit point, ask whether to commit and push, naming the remote and destination branch. Recommend either a direct commit to `master` or a branch and PR based on the diff and branch state. For a PR, also agree on its base, such as `master` or the active development branch. Settle these destinations before committing. Follow `AGENTS.md` for authorization and local-only exceptions; already-authorized actions do not need another confirmation.

Use the [commit-message conventions](../Contributing.md#commit-messages) when a commit is authorized, and follow the [merge strategy](../Contributing.md#merge-strategy) when separately authorized to merge. The handoff does not need a pasted suggested commit message unless the user asks for one. A review-only task with no changes does not need staging or a commit question.

For work delivered through a PR, the creating agent's responsibility ends after committing and pushing the approved changes, opening the PR, and handing it over. Include the PR reference, validation results, and any known outstanding work; distinguish completed checks from checks that are pending or were not run. The maintainer coordinates subsequent review and merging. The creating agent does not need to keep the session open, wait for another agent's review, or monitor the PR unless explicitly asked. Later feedback can be handled in another session when assigned.

## Tool Selection and Environment

- [DataLinq.Dev.CLI](DataLinq.Dev.CLI.md): environment diagnosis and restore/build/direct test wrapping.
- [DataLinq.Testing.CLI](DataLinq.Testing.CLI.md): focused TUnit runs, feedback plans, and provider infrastructure.
- [DataLinq.Benchmark.CLI](DataLinq.Benchmark.CLI.md): benchmark discovery, runs, and comparisons when performance is in scope.

Human-facing tool examples normally run from `src`; this page's examples run from the repository root. Explicit Dev CLI target arguments remain repo-root-relative in either case.

On native Windows in a sandbox, use the root wrapper. Bootstrap the CLI's restore assets before the first wrapped `run`, because the wrapper adds `--no-restore`:

```powershell
./scripts/dotnet-sandbox.ps1 restore src/DataLinq.Testing.CLI/DataLinq.Testing.CLI.csproj
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --plan focused --suite unit --filter "/*/*/CacheNotificationManagerTests/*" --output failures
```

The same bootstrap applies to the Dev and Benchmark CLI projects when needed. It is setup for a cold checkout or changed dependencies, not a step to repeat for every edit. On other platforms, use the normal `dotnet run --project ...` entry points and the repo-local CLI profiles.

See [Native Windows Sandbox Build Notes](Internal%20Tooling.md#native-windows-sandbox-build-notes) for restore/cache failures, WebAssembly task-host issues, and server-backed connectivity. Follow the current execution environment's permission rules; a repo troubleshooting recipe cannot enable unavailable escalation.

## Documentation Boundaries

- `README.md`: GitHub-facing overview.
- Root `index.md`: DocFX website homepage.
- `docs/index.md`: documentation intro.
- Normal `docs/` pages: shipped product and contributor documentation.
- `docs/dev-plans/`: design records, plans, and implementation history.

Use code, tests, and normal docs for current behavior. Use dev plans for future intent and implementation rationale, keeping their status explicit. If navigation or presentation changes, inspect generated `_site` output. Serve pages over HTTP for browser checks.

## Release and Packaging Notes

Use `publish-nuget.ps1` for release work and prompt for secrets at execution time. Package publishing requires its own explicit authorization; commit approval does not authorize it.

Treat `CHANGELOG.md` as generated output. Author the release description through the release-note workflow, then use `generate-changelog.ps1` to regenerate the changelog from published GitHub releases. Commit subjects are not the changelog's authoring surface.
