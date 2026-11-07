# Internal Tooling

This page is the canonical map for the repo-local developer tooling.

If you are working inside the DataLinq repo, these are the tools that matter:

If you are setting up a machine for the first time, start with [Dev and Test Environment](Dev%20and%20Test%20Environment.md) before diving into individual tool behavior.

| Tool | Use it for | Do not use it for |
| --- | --- | --- |
| `DataLinq.Dev.CLI` | repo-local `dotnet` execution, environment diagnosis, restore/build/test wrapping, and release-evidence reports | provider-matrix orchestration or benchmarks |
| `DataLinq.Testing.CLI` | test infrastructure lifecycle, target aliases, batched provider runs, suite orchestration | normal restore/build workflows |
| `DataLinq.Benchmark.CLI` | benchmark discovery, benchmark runs, history artifacts, regression comparison | ordinary tests or direct build workflows |

## Why These Tools Exist

The repo has already paid the price for ad hoc tooling:

- raw `dotnet` output is noisy
- environment behavior is inconsistent across local machines, CI, and sandboxed agents
- Podman orchestration and provider selection are too easy to get subtly wrong
- benchmark runs are only useful when their inputs and artifacts are stable

These tools exist to make the sane path the default path.

## Start Here

Use this decision table:

| You want to... | Tool |
| --- | --- |
| check whether the local `dotnet` environment is sane | [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md) |
| restore or build the repo with repo-local caches and concise output | [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md) |
| run a single project or solution through `dotnet test` | [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md) |
| inspect a package candidate, smoke it as a consumer, compare its public API, or produce constrained-runtime evidence | [`DataLinq.Dev.CLI`](DataLinq.Dev.CLI.md) |
| bring test containers up or down | [`DataLinq.Testing.CLI`](DataLinq.Testing.CLI.md) |
| run focused tests or feedback plans, or select provider targets | [`DataLinq.Testing.CLI`](DataLinq.Testing.CLI.md) |
| run or compare benchmarks | [`DataLinq.Benchmark.CLI`](DataLinq.Benchmark.CLI.md) |
| audit direct/transitive dependencies against the online advisory feed | `scripts/audit-dependencies.ps1`, documented in [CI Test Lanes](CI%20Test%20Lanes.md#dependency-advisory-audit) |

For the local test-selection policy, see the [contributor feedback loop](../Contributing.md#local-feedback-loop). Run focused tests while editing, `quick` at a code stopping point, and consider `latest` locally for MySQL/MariaDB changes. Broader coverage normally runs in CI.

## Child process execution

Shared developer-tool execution drains stdout and stderr concurrently, including
the Podman socket transport's binary output. A verbose child can therefore fill
either pipe without blocking the parent on the other pipe.

`ExternalProcessRunner.ExecuteAsync` accepts an optional timeout and cancellation
token. A timeout throws `TimeoutException`; caller cancellation preserves
`OperationCanceledException`. Failure cleanup kills a running child process tree,
waits for exit, and closes and observes the pipe reads. Cleanup failure is reported
alongside the original failure. The synchronous `Execute` entry point retains its
existing signature and has no implicit overall timeout. Podman CLI and socket
commands have a 30-minute timeout, including image pulls. Output remains buffered
in memory, so callers should avoid commands with unlimited output.

## Native Windows Sandbox Build Notes

These notes apply to native Windows sandbox execution. Use the normal repo-local CLI entry points on other platforms. When a sandbox is available, try it first; retry with escalation only after a likely sandbox, network, cache, or permission failure and only if the execution environment allows that escalation.

### Wrapper and Cold-Checkout Bootstrap

Run the wrapper from the repository root. It pins `DOTNET_CLI_HOME`, `APPDATA`, `LOCALAPPDATA`, temp paths, NuGet HTTP cache, NuGet scratch space, and restore configuration to the repo-local `.dotnet-cli/` tree. Before replacing `LOCALAPPDATA`, it preserves the real Podman executable and machine socket paths in `DATALINQ_TEST_PODMAN_PATH` and `DATALINQ_TEST_PODMAN_SOCKET`. Keep those overrides when changing the wrapper: the Testing CLI normally derives Podman defaults from `LOCALAPPDATA`.

The wrapper adds `--no-restore` to `run`, `build`, `test`, `pack`, and `publish`. Restore the entry CLI explicitly before its first wrapped run on a cold checkout, or after its dependencies change:

```powershell
./scripts/dotnet-sandbox.ps1 restore src/DataLinq.Testing.CLI/DataLinq.Testing.CLI.csproj
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --plan focused --suite unit --filter "/*/*/CacheNotificationManagerTests/*" --output failures
```

Restore `src/DataLinq.Dev.CLI/DataLinq.Dev.CLI.csproj` or `src/DataLinq.Benchmark.CLI/DataLinq.Benchmark.CLI.csproj` in the same way before using those entry points if their restore assets are missing. This bootstrap does not require restoring or building the entire solution for each code edit.

### Restore and Package Cache

`.\scripts\dotnet-sandbox.ps1` intentionally redirects .NET and NuGet state into the repo-local `.dotnet-cli/` tree. On a cold cache, sandboxed restore can fail with warnings/errors like:

```text
NU1801: Unable to load the service index for source https://api.nuget.org/v3/index.json.
NU1101: Unable to find package Spectre.Console.
NU1101: Unable to find package System.CommandLine.
```

These failures can indicate blocked NuGet.org access plus missing cached packages. A cold build missing package-backed types such as `Spectre.Console` or `System.CommandLine` is not evidence of a source defect until restore succeeds. Retry the same explicit restore through the wrapper with sandbox escalation when permitted, then return to sandboxed build/test work. If escalation is unavailable, report the environment limitation rather than changing package references to work around it.

### WebAssembly Task Host

`src/DataLinq.BlazorWasm/DataLinq.BlazorWasm.csproj` currently exercises the .NET WebAssembly SDK. In the Codex sandbox on native Windows, the WebAssembly build can fail while running `MarshalingPInvokeScanner`:

```text
MSB4216: Could not run the "MarshalingPInvokeScanner" task because MSBuild could not create or connect to a task host with runtime "NET" and architecture "x64".
MSB4027: The "MarshalingPInvokeScanner" task generated invalid items from the "IncompatibleAssemblies" output parameter.
```

The full solution build may surface the same underlying problem less helpfully as:

```text
Build FAILED.
    0 Warning(s)
    0 Error(s)
```

Verify the same affected-project or solution build outside the sandbox when escalation is permitted before classifying this as a repo regression. If it succeeds there, report the sandbox limitation separately from source warnings or failures. If verification is unavailable, report that uncertainty.

The `WASM0001` warnings emitted by the successful outside-sandbox build are different. They are real SDK warnings about `SQLitePCLRaw.provider.e_sqlite3` exposing varargs native SQLite functions that WebAssembly cannot call safely. Treat those as product/runtime compatibility work. Do not blanket-suppress them without proving the affected functions are unreachable or changing the WebAssembly SQLite provider.

`DataLinq.Dev.CLI size-report` runs WebAssembly browser smoke through Playwright when a publish succeeds. It uses a locally installed Chromium-compatible browser, not a checked-in browser binary. Set `DATALINQ_BROWSER_PATH` if Edge, Chrome, or Chromium is installed in a non-standard location.

### Server-Backed Tests

For sandboxed server-backed Testing CLI runs on native Windows, set `DATALINQ_TEST_DB_HOST=127.0.0.1` for the command. Loopback reaches Podman through its `wslrelay` listeners while the sandbox can block the Podman VM address. The matrix uses ports `13307` through `13312` to avoid common local MySQL/MariaDB services.

This example assumes the Testing CLI has been built in Debug and the target is available. It restores the caller's previous host override afterwards:

```powershell
$previousDbHost = $env:DATALINQ_TEST_DB_HOST
try {
    $env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
    ./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -c Debug --no-build -- run --plan focused --suite mysql --targets mysql-9.7 --filter "/*/*/<TestClass>/<TestMethod>" --output failures
}
finally {
    if ($null -eq $previousDbHost) {
        Remove-Item Env:DATALINQ_TEST_DB_HOST -ErrorAction SilentlyContinue
    }
    else {
        $env:DATALINQ_TEST_DB_HOST = $previousDbHost
    }
}
```

Replace the filter placeholders with the relevant TUnit class and method. Server-backed `run` commands refresh `artifacts/testdata/testinfra-state.json` from the containers actually running, so targeted verification should not leave the state narrowed to one server target. If connectivity looks wrong, inspect listener ownership for the configured port and confirm it belongs to `wslrelay`. Repair or recreate the affected Podman test targets outside the sandbox when permitted and necessary; do not disturb unrelated services.

### Warning and Build Audits

An explicit warning/build audit needs fresh compilation rather than an up-to-date incremental result. From the repository root:

```powershell
./scripts/dotnet-sandbox.ps1 restore src/DataLinq.sln -v minimal
./scripts/dotnet-sandbox.ps1 build src/DataLinq.sln -c Debug -v minimal --no-incremental
```

This broader build is for the audit task, not the normal edit/test loop. Apply the WebAssembly task-host diagnosis above if the sandboxed solution build fails without useful errors.

## Canonical Tool Pages

- [DataLinq.Dev.CLI](DataLinq.Dev.CLI.md)
- [DataLinq.Testing.CLI](DataLinq.Testing.CLI.md)
- [DataLinq.Benchmark.CLI](DataLinq.Benchmark.CLI.md)

## Artifact Conventions

The tools deliberately write artifacts into predictable repo-local locations:

- `artifacts/dev/`
  restore/build/test wrapper artifacts, binary logs, and default release-evidence report roots
- `artifacts/release/`
  explicitly retained candidate evidence, including package, consumer, API, and constrained-runtime reports
- `artifacts/testdata/`
  test infrastructure runtime state and lifecycle logs
- `artifacts/test-results/`
  test-run summaries, build logs, test-host logs, HTML/TRX reports, and fixture telemetry
- `artifacts/benchmarks/`
  benchmark reports, summaries, comparisons, telemetry, and raw logs

Those paths are part of the workflow. If you bypass the tools, you usually also bypass the artifacts that make failures diagnosable later.
