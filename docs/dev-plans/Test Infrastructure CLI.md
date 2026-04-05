> [!WARNING]
> This document is roadmap and implementation planning material. It describes planned tooling and migration stages rather than current shipped behavior.
# Specification: Cross-Platform Test Infrastructure CLI

**Status:** Implemented  
**Goal:** Replace the growing PowerShell-based test infrastructure scripts with a cross-platform .NET CLI that owns target selection, Podman orchestration, local matrix runs, and interactive test-environment workflows.

## 1. Why We Are Changing Direction

The current PowerShell scripts were good enough to prove the workflow. They are no longer a good long-term home for it.

What has changed:

* The test environment now has real matrix behavior instead of a single fixed MySQL/MariaDB pair.
* Target selection now matters as much as raw container startup.
* We want the same tooling to run on Windows, Linux, and macOS.
* We want both interactive and non-interactive workflows.
* SQLite and SQLite in-memory should be treated as first-class test targets, not special booleans bolted onto server batches.

That is already bigger than a script layer should comfortably own.

The blunt truth is this: if we keep extending the PowerShell scripts, we will rebuild a fragile application inside a shell language.

## 2. Decisions Already Made

### 2.1. New Project Name

The new tool will live in:

* `src/DataLinq.Testing.CLI`

This matches the naming style already used by `DataLinq.CLI` and is clearer than names like `Testrunner` or `TestInfra`.

### 2.2. Technology Choice

The new tool should be a normal .NET console app, not a .NET 10 file-based app.

Why:

* File-based apps are ideal for small, single-file utilities.
* This tool already needs multiple concerns and will grow further.
* A proper project gives us normal references, tests, packaging options, and maintainability.

### 2.3. CLI Libraries

Use:

* `System.CommandLine` for command parsing, validation, help text, aliases, and shell completion support
* `Spectre.Console` for interactive prompts, selection menus, confirmations, and readable console output

Do **not** use:

* `Spectre.Console.Cli` as the main command parser
* another third-party parser unless `System.CommandLine` hits a concrete blocker

Reasoning:

* `System.CommandLine` is now stable and is the most natural fit for .NET-native CLI tooling.
* `Spectre.Console` is excellent for interactive UX, but its CLI layer is not the strongest reason to adopt it here.
* The clean split is parser/invocation in `System.CommandLine`, interactive rendering in `Spectre.Console`.

### 2.4. Remove the PowerShell Scripts

We do **not** intend to keep the current PowerShell scripts as permanent wrappers.

Migration principle:

* Build the new CLI until it reaches feature parity for the current workflows
* Remove the PowerShell scripts once the new CLI is proven

This is the right tradeoff. Keeping both forever would just create two sources of truth.

## 3. Target Model

The new CLI should stop centering its UX around “profiles.”

Internally, the system should be target-based.

### 3.1. First-Class Targets

The CLI should treat all of these as peers:

* `sqlite-file`
* `sqlite-memory`
* `mysql-8.4`
* `mariadb-10.11`
* `mariadb-11.4`
* `mariadb-11.8`

Important nuance:

* SQLite targets do not need Podman
* server targets do

But the user should not have to think in two unrelated selection models.

### 3.2. Aliases

The user-facing presets should be:

* `quick`
  `sqlite-file`, `sqlite-memory`
* `latest`
  `sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`
* `all`
  every supported target

This is cleaner than the current “profile + include SQLite” logic.

### 3.3. Why This Is Better

The target model fixes several ugly behaviors at once:

* SQLite and SQLite in-memory become visible in summaries and batch plans
* MySQL 8.4 does not need to be rerun three times just because MariaDB has three supported LTS lines
* batching becomes target-based instead of profile-based
* the mental model becomes obvious: aliases expand to targets

## 4. Command Surface

The initial command surface should be small and explicit.

Recommended commands:

* `list`
  Show known targets, aliases, and current runtime state
* `up`
  Provision the selected server targets and persist runtime state
* `wait`
  Wait for selected server targets to become ready
* `down`
  Stop or remove selected server targets
* `reset`
  Tear down and recreate selected server targets
* `run`
  Execute the compliance suite against selected targets, optionally in batches

### 4.1. Selection Options

Commands should support either:

* `--alias quick|latest|all`
* `--targets sqlite-file,sqlite-memory,mysql-8.4`

These should be mutually exclusive.

### 4.2. Interactive Mode

The CLI should support:

* no-argument interactive entry
* `--interactive` on relevant commands

Interactive mode should prompt for:

* command
* alias or targets
* batch size where relevant
* whether to keep server targets running afterward

Crucial rule:

* interactive mode must be a UI over the exact same command model
* it must not have its own hidden behavior

### 4.3. Non-Interactive Mode

The CLI must remain fully scriptable.

Examples:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- list
dotnet run --project src/DataLinq.Testing.CLI -- up --alias latest
dotnet run --project src/DataLinq.Testing.CLI -- run --alias quick
dotnet run --project src/DataLinq.Testing.CLI -- run --alias all --batch-size 2
dotnet run --project src/DataLinq.Testing.CLI -- run --targets sqlite-file,sqlite-memory,mysql-8.4
```

## 5. Runtime State

The new CLI should own one runtime state file, likely under:

* `artifacts/testdata/testinfra-state.json`

This should replace the current Podman-settings-centric shape with a more honest model.

Recommended persisted data:

* selected targets currently provisioned
* resolved host
* per-target host port
* credentials used by the harness
* timestamps or state version if needed

This file should describe the active target set uniformly, whether targets are SQLite or Podman-backed servers.

## 6. Podman Ownership

The new CLI should own all Podman orchestration directly.

Responsibilities:

* resolve target images and host ports from the matrix
* decide container names deterministically
* start containers
* wait for readiness
* provision elevated test users
* stop and remove containers
* persist the resolved runtime state

Platform-specific behavior should be isolated to:

* locating and invoking `podman`
* resolving the published host correctly, especially on Windows with Podman machine

Everything else should be platform-neutral .NET code.

## 7. Shell Completion

We do want tab completion.

`System.CommandLine` supports completion, but the UX is better when the tool can be invoked as its own command rather than only through `dotnet run --project ...`.

Short-term plan:

* Build the CLI for `dotnet run --project ... -- ...`

Longer-term option:

* expose it as a local tool or published binary for cleaner shell completion and easier everyday use

Completion should include:

* command names
* alias names
* target ids

## 8. Runsettings Simplification

Once the new CLI is in place, we should simplify the runsettings surface to only reflect the user-facing aliases:

* `quick`
* `latest`
* `all`

That means the current MariaDB-version-specific runsettings files are transitional and should be removed once the alias-based flow is stable.

## 9. Implementation Stages

This should be done in deliberate stages, not as one giant replacement patch.

### Stage 1: Project Skeleton and Command Contract

Current status: **Done**

Deliverables:

* `src/DataLinq.Testing.CLI`
* package references to `System.CommandLine` and `Spectre.Console`
* basic `list` command
* explicit alias and target catalog

Exit criteria:

* we can list aliases and targets from the new CLI
* the command surface is stable enough to build on

### Stage 2: Shared Target and Alias Model

Current status: **Done**

Deliverables:

* shared target definitions for SQLite and server targets
* alias expansion for `quick`, `latest`, and `all`
* reuse of the existing matrix file for server targets

Exit criteria:

* the CLI can resolve a target set without touching Podman yet

### Stage 3: Runtime State and Podman Abstraction

Current status: **Done**

Deliverables:

* new runtime state model
* cross-platform Podman process abstraction
* host resolution logic
* deterministic container naming and port handling

Exit criteria:

* the CLI can represent the current environment consistently across Windows, Linux, and macOS

### Stage 4: `up`, `wait`, `down`, `reset`

Current status: **Done**

Deliverables:

* server-target provisioning commands
* readiness checks
* teardown behavior
* runtime state persistence

Exit criteria:

* the CLI can replace the current environment lifecycle scripts

### Stage 5: `run` and Batching

Current status: **Done for non-interactive flows**

Deliverables:

* test execution command
* target batching
* deduplicated SQLite and server coverage
* summary output

Exit criteria:

* `all` can run once in one pass
* `all --batch-size 2` can fan out without rerunning SQLite or MySQL needlessly

### Stage 6: Interactive Flows

Current status: **Done**

Deliverables:

* interactive mode with `Spectre.Console`
* selection prompts
* confirmations
* readable summaries

Exit criteria:

* a developer can use the CLI comfortably without memorizing every flag

### Stage 7: Cutover and Cleanup

Current status: **Done**

Deliverables:

* remove the current PowerShell scripts
* reduce runsettings to `quick`, `latest`, and `all`
* update the docs and examples to use the new CLI

Exit criteria:

* one source of truth remains for local and CI test infra orchestration

## 10. Implemented End State

The repo now has:

1. `src/DataLinq.Testing.CLI` as the cross-platform entry point
2. first-class target aliases `quick`, `latest`, and `all`
3. interactive and non-interactive command flows
4. target-based batching and runtime-state persistence
5. three solution-wide runsettings files that match the alias model
6. no PowerShell orchestration scripts left in the supported workflow

The next work should happen in the test migration itself, not by reopening the infrastructure abstraction unless a real limitation appears.
