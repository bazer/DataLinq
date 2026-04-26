# Build Environment and Output Control

> [!WARNING]
> This document is an implementation plan for developer tooling. It is not user-facing product documentation.

**Status:** Active implementation document  
**Goal:** Make `dotnet`/MSBuild/NuGet execution reliable in constrained environments and make build/test output concise, structured, and useful by default.

## 0. Current State

The first usable slice is now implemented.

Shipped in this slice:

- `src/DataLinq.DevTools`
  Shared repo-local environment/profile handling, raw artifact logging, process execution, and build-output classification.
- `src/DataLinq.Dev.CLI`
  `doctor`, `restore`, `build`, and `test` commands.
- default repo-local execution roots under `.dotnet/`
- raw artifacts under `artifacts/dev/`
- concise `quiet`, `summary`, `errors`, `failures`, `raw`, and `diag` output modes
- reuse of the shared process/environment layer from `DataLinq.Benchmark.CLI` and `DataLinq.Testing.CLI`

Validated outcomes from implementation:

- the wrapper can build itself cleanly from a repo-local cache
- the wrapper can classify broken-sandbox restore/build failures without dumping raw logs by default
- a one-time non-sandbox restore can hydrate the repo-local cache when exact package versions are missing

Still intentionally left for later:

- CI adoption
- any generic `exec` passthrough
- deeper output shaping beyond distinct diagnostics and test-failure extraction

## 1. Problem Statement

Right now we have two different problems tangled together:

1. The execution environment is unreliable for Codex and other sandboxed agents.
2. The raw build and test output is too noisy to be a sane default.

Those problems overlap, but they are not the same problem.

The environment issue is the more fundamental one. A perfectly filtered build log is still useless if the tool is failing because it cannot write to the default home directory, cannot read the default `NuGet.Config`, or cannot restore packages in a network-restricted run.

The output issue matters because even when `dotnet` does run, the default output is often bloated, repetitive, and bad at surfacing the actual root cause. A build wrapper that only proxies raw `stdout` and `stderr` is not a serious developer experience.

## 2. Observed Problems in the Current Environment

These are not hypothetical. They were observed directly while trying to run normal DataLinq workflows inside Codex.

### 2.1. First-Time Use and Home-Directory Writes

Observed failure mode:

- `dotnet` tried to create first-time-use sentinel files under a default user profile path that was not writable in the sandbox.

Why this is bad:

- it is unrelated to the actual repo task
- it forces ad hoc retry logic and environment tweaking
- it creates noisy failures before restore/build even starts

### 2.2. Global User Config Paths Leak into the Sandbox

Observed failure mode:

- `dotnet` attempted to read `C:\Users\...\AppData\Roaming\NuGet\NuGet.Config`
- that path was inaccessible in the sandbox

Why this is bad:

- the repo should not depend on ambient machine state just to restore or build
- it makes runs nondeterministic across Codex, CI, and different developer machines

### 2.3. Network-Restricted Restores Fail in a Messy Way

Observed failure mode:

- restore attempted to reach `https://api.nuget.org/v3/index.json`
- network restrictions caused `NU1301` failures
- some packages were already cached, others were not

Why this is bad:

- the failure is real, but the output is much noisier than it needs to be
- there is currently no single offline-aware entry point that says “these packages are already cached” vs “these packages are missing”

### 2.4. SDK/Host Problems Are Not Diagnosed Early

Observed failure mode:

- solution restore/build hit a missing SDK workload resolver component and surfaced a mostly useless “Build FAILED” shape

Why this is bad:

- this is an environment problem, not a repo-code problem
- the current flow does not separate “host is broken” from “project is broken”

### 2.5. Raw Build Output Has Terrible Signal-to-Noise

Observed failure modes:

- repeated copies of the same NuGet error
- warnings that are irrelevant to the real failure
- “Build FAILED” summaries that hide the actionable cause in preceding output
- giant diagnostic logs that are useful only after you already know you need them

Why this is bad:

- the default output spends too many tokens saying too little
- it trains the agent to try one-off flags repeatedly instead of using a stable wrapper

## 3. What the Repo Already Has

The repo is not starting from zero. It already contains some of the right ideas, just in the wrong places.

### 3.1. Benchmark CLI Already Uses a Repo-Local Execution Profile

`src/DataLinq.Benchmark.CLI/BenchmarkCliSettings.cs` already sets:

- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`
- repo-local `DOTNET_CLI_HOME`
- repo-local `APPDATA`, `LOCALAPPDATA`, `HOME`, and `USERPROFILE`
- repo-controlled `NUGET_PACKAGES`
- `NuGetAudit=false`

That is exactly the kind of environment normalization we want. The problem is that this logic is isolated inside the benchmark CLI instead of being reused across build, restore, and test flows.

### 3.2. Benchmark CLI Already Treats Raw Logs as Artifacts

`src/DataLinq.Benchmark.CLI/BenchmarkHarnessRunner.cs` already does several smart things:

- writes raw logs to `artifacts/`
- defaults to quieter verbosity
- shows curated summaries instead of dumping everything
- keeps detailed artifacts available for drill-down

That is the right mental model. The raw log should exist, but it should not be the default user experience.

### 3.3. Test Infrastructure CLI Has a Process Runner but Not a Real Build Profile

`src/DataLinq.Testing.CLI/Infrastructure/ExternalProcessRunner.cs` is a simple process wrapper, but it does not centralize:

- repo-local environment isolation
- offline-aware restore behavior
- output classification or deduplication
- raw-log artifacting for every command family

So the repo currently has the low-level primitive, but not the higher-level execution policy.

## 4. Opinionated Take on RTK

The provided project, [rtk](https://github.com/rtk-ai/rtk), is relevant, but only partly.

What it gets right:

- command-family-aware output filtering is a real idea, not fluff
- “failures only” or “errors only” modes are exactly the right default direction
- keeping token usage low by transforming output before it reaches the agent is sane

What it does **not** solve for us:

- repo-local `dotnet` environment isolation
- NuGet/Home/AppData path normalization
- offline-aware restore behavior for this specific repo
- detection of broken SDK installs or host-specific .NET issues

My opinion is simple: `rtk` is good inspiration, not a complete answer.

Importing it as the primary solution would be a mistake right now because:

- it adds an external dependency for a problem we can solve with repo-local tooling
- it is broader than we need
- it still would not fix the most painful failure modes we observed

The right move is to copy the good idea, not the whole product.

## 5. Design Principles

### 5.1. Reliability First, Filtering Second

Do not build a pretty output wrapper on top of a flaky execution environment.

### 5.2. Raw Logs Must Exist, but Not Dominate

Every wrapped command should preserve a raw artifact, but the default console experience should be summarized.

### 5.3. Environment Profiles Must Be Explicit

We need named execution profiles instead of random environment overrides:

- normal local
- repo-isolated
- sandbox/offline
- CI

### 5.4. Output Modes Must Be User-Selectable

The tool should support at least:

- `quiet`
  Success: one-line summary only.
- `summary`
  Show the top-level outcome, project counts, duration, warnings count, and artifact path.
- `errors`
  Show only distinct errors plus a short summary.
- `failures`
  Show only test failures and failing command summaries.
- `raw`
  Passthrough output with minimal interference.
- `diag`
  Explicitly generate full diagnostic artifacts such as `.binlog` or detailed logs.

### 5.5. The Wrapper Must Be Honest

If a build fails because the SDK is broken, say that.
If it fails because a package is not cached and the network is unavailable, say that.
Do not pretend every failure is a compiler error.

## 6. Recommended Solution

### 6.1. Create a Shared Dotnet Execution Layer

We should extract a shared internal execution layer instead of duplicating the benchmark CLI logic again.

Recommended responsibilities:

- resolve repo root
- create repo-local execution directories
- generate environment-variable sets for each profile
- choose default `dotnet` arguments for restore/build/test
- write raw logs and optional binlogs
- return structured command results instead of only raw text

This should become the single source of truth for how DataLinq runs `dotnet` in constrained environments.

### 6.2. Add a Dedicated Developer Tooling CLI

I do **not** think this belongs inside `DataLinq.Testing.CLI`.

That tool already has a clear purpose: test infrastructure orchestration and suite execution.

Recommended new tool:

- `src/DataLinq.Dev.CLI`

Suggested commands:

- `doctor`
  Diagnose SDK, writable paths, NuGet config resolution, package cache readiness, and network assumptions.
- `restore`
  Wrapper for repo restore.
- `build`
  Wrapper for project/solution build.
- `test`
  Wrapper for `dotnet test` or selected test projects.
- `exec`
  Optional escape hatch for wrapped arbitrary `dotnet` commands using the same environment profile.

This keeps responsibilities clean:

- `DataLinq.Testing.CLI` remains about test infra and suite orchestration
- `DataLinq.Dev.CLI` becomes the stable entry point for developer-facing `dotnet` execution

### 6.3. Reuse the Benchmark CLI Patterns

We should intentionally reuse:

- repo-local env normalization
- raw artifact logging
- quiet-by-default output
- curated summaries on success and failure

This is one of those cases where the repo already solved part of the problem once. Not reusing that would be silly.

## 7. Environment Hardening Plan

### Phase 1: Codify the Repo-Local Execution Profile

Define a standard profile that sets:

- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`
- repo-local `DOTNET_CLI_HOME`
- repo-local `APPDATA`
- repo-local `LOCALAPPDATA`
- repo-local `HOME`
- repo-local `USERPROFILE`
- repo-local or explicitly chosen `NUGET_PACKAGES`
- `DOTNET_NOLOGO=1`
- `DOTNET_CLI_UI_LANGUAGE=en`
- `NuGetAudit=false`

Also evaluate:

- `DOTNET_MULTILEVEL_LOOKUP=0`
- explicit local `NuGet.Config`
- optional offline mode flags

Exit criteria:

- the wrapper never depends on inaccessible ambient user-profile paths

### Phase 2: Add a `doctor` Command

The tool should detect and classify:

- missing or unsupported SDK
- broken SDK/workload resolver installation
- inaccessible home/config paths
- missing package cache entries in offline mode
- network-required restore attempts

Exit criteria:

- before running restore/build, we can explain the environment state in one concise report

### Phase 3: Offline-Aware Restore Strategy

Do not blindly enable “ignore failed sources” everywhere.

The wrapper should support two explicit modes:

- online/default
  Normal restore behavior.
- offline/sandbox
  Use cached packages when possible, suppress useless audit/network noise, and fail with a concise “missing cached packages” summary when necessary.

Exit criteria:

- offline failures are short, accurate, and actionable

## 8. Output Control Plan

### Phase 4: Structured Parsing and Deduplication

Implement parsers/classifiers for:

- MSBuild errors and warnings
- NuGet errors and warnings
- compiler diagnostics
- test failures
- obvious environment failures

Behavior:

- exact duplicate lines should collapse into one entry with a count
- distinct errors should be grouped by project/file/code when possible
- stack traces should be collapsed by default unless explicitly requested

Exit criteria:

- repeated noise no longer floods the console

### Phase 5: Default Presentation Modes

Default success output should look more like:

- `OK build src/DataLinq.sln (15 projects, 0 errors, 4 warnings, 22.4s)`
- `Raw log: artifacts/dev/build-20260419-143122.log`

Default failure output should look more like:

1. top-level failure classification
2. distinct errors
3. optional short warning summary
4. raw log and binlog paths

For tests:

- show only failed test names, target/project context, and short messages by default

Exit criteria:

- most successful runs are one or two lines
- most failed runs fit in a short, high-signal summary before linking to raw artifacts

### Phase 6: Artifact Strategy

Store raw artifacts under a predictable repo path, for example:

- `artifacts/dev/restore-*.log`
- `artifacts/dev/build-*.log`
- `artifacts/dev/test-*.log`
- `artifacts/dev/*.binlog`

Raw logs should be easy to inspect, but not spammed into the conversation by default.

## 9. Suggested Implementation Order

1. Extract the benchmark CLI environment/profile logic into a shared execution layer.
2. Build a minimal `DataLinq.Dev.CLI doctor` command.
3. Add wrapped `restore` and `build` commands with `quiet`, `summary`, and `errors` modes.
4. Add log artifacting and optional binlog generation.
5. Add `test` wrapping with failure-only summaries.
6. Decide later whether a generic command proxy mode is worth adding.

That sequence matters.

Starting with a broad generic proxy is the wrong move. Start with the commands we actually need and the failure modes we actually hit.

## 10. Resolved Decisions

These were decided during implementation:

- `NUGET_PACKAGES` defaults to a repo-local cache under `.dotnet/.nuget/packages`
- the new tool lives in `src/DataLinq.Dev.CLI`
- output parsing and process/environment handling were extracted into `src/DataLinq.DevTools` and reused by `DataLinq.Testing.CLI` and `DataLinq.Benchmark.CLI`
- `.binlog` generation defaults to `auto`, which means generate it during build runs and surface it primarily on failure

Still open:

- whether CI should switch to the wrapper immediately or only after a follow-up hardening pass
- whether an `exec` escape hatch is worth adding at all

## 11. Recommended Decision

My recommendation:

- write our own repo-local tool
- borrow ideas from `rtk`, but do not depend on it
- centralize environment isolation first
- make concise output the default, raw logs the fallback

That is the practical answer.

`rtk` is useful because it proves the UX idea is real. But the repo already contains the seeds of a better DataLinq-specific solution, and that solution can address both the sandbox reliability problem and the token-noise problem in one coherent design.
