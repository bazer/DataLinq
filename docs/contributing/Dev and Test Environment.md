# Dev and Test Environment

This page is the fresh-machine path for working on DataLinq itself.

If you only want to use DataLinq from another application, start with the normal getting-started docs instead. This page is for contributors who need to build the repo, run the TUnit suites, and bring the MySQL/MariaDB test containers up.

## TLDR

For the normal Windows contributor path, install Git, the .NET 10 SDK, and Podman with WSL 2 support. Then run:

```powershell
git clone https://github.com/bazer/DataLinq.git
cd DataLinq/src

dotnet workload restore

podman machine init # first time only; skip if the default machine already exists
podman machine start

dotnet run --project DataLinq.Dev.CLI -- build

dotnet run --project DataLinq.Testing.CLI -- run --alias latest --batch-size 4 --output failures
```

`build` restores packages as part of normal .NET behavior. The test CLI brings missing server containers up before running server-backed batches.

Use `--alias quick` for the SQLite-only lane and `--alias all` before broad provider changes.

## Required Tools

Install these first:

- Git
- .NET 10 SDK
- Podman

On Windows, install Podman with the WSL 2 backend. The server-backed tests use Linux containers, not Windows containers.

You do not need:

- Docker
- Docker Compose
- `podman compose`
- a locally installed MySQL or MariaDB server
- manually created test databases

The repo-local test CLI owns the database containers, ports, users, passwords, and runtime state.

## Windows Prerequisites

On Windows, make sure WSL 2 is available before relying on Podman:

```powershell
wsl --status
```

If WSL is not installed yet, install it from an elevated PowerShell session:

```powershell
wsl --install
```

Then install Podman and create or start the default Podman machine:

```powershell
podman machine init
podman machine start
podman machine list
```

If `podman machine init` says the machine already exists, that is fine. Start it and confirm it is running.

```powershell
podman machine start
podman machine list
```

Healthy output should show a running machine, usually named `podman-machine-default`.

## Clone and Build

Clone the repo:

```powershell
git clone https://github.com/bazer/DataLinq.git
cd DataLinq/src
```

Check the installed .NET SDKs:

```powershell
dotnet --info
```

You need a .NET 10 SDK because the active test projects target `net10.0`. The library packages also target .NET 8 and .NET 9, but the contributor test lane is a .NET 10 lane.

Restore the WebAssembly workload used by the Blazor WebAssembly smoke project. Run this from the `src` folder so `dotnet` discovers `DataLinq.sln`:

```powershell
dotnet workload restore
```

.NET workloads are resolved per SDK feature band. If a new .NET SDK is installed or Visual Studio starts using a different SDK feature band, rerun the workload restore command. The command should report `wasm-tools` for the active SDK:

```powershell
dotnet workload list
```

Run the repo-local environment check:

```powershell
dotnet run --project DataLinq.Dev.CLI -- doctor --profile repo
```

Restore and build through the developer wrapper:

```powershell
dotnet run --project DataLinq.Dev.CLI -- restore
dotnet run --project DataLinq.Dev.CLI -- build
```

Raw `dotnet restore` and `dotnet build` can work, but the wrapper is the better default because it keeps repo-local caches and artifacts predictable.

## Check Podman

Confirm Podman is callable:

```powershell
podman --version
podman info
```

On Windows, `podman info` should report a Windows client talking to a Linux host through the Podman machine. If the machine is stopped, start it:

```powershell
podman machine start
```

## Test Target Aliases

The test CLI has three useful aliases:

- `quick`
  Runs only local SQLite targets. No Podman required.
- `latest`
  Runs SQLite plus MySQL 8.4 and MariaDB 11.8. This is the normal server-backed lane.
- `all`
  Runs every supported target: SQLite, MySQL 8.4, MariaDB 10.11, MariaDB 11.4, and MariaDB 11.8.

The central target inventory is the [Test Provider Matrix](../support-matrices/Test%20Provider%20Matrix.md).

List the suites, aliases, targets, and current runtime state:

```powershell
dotnet run --project DataLinq.Testing.CLI -- list
```

## Bring Containers Up

Start the normal latest server-backed environment:

```powershell
dotnet run --project DataLinq.Testing.CLI -- up --alias latest
```

Start the full server matrix:

```powershell
dotnet run --project DataLinq.Testing.CLI -- up --alias all
```

Start specific targets:

```powershell
dotnet run --project DataLinq.Testing.CLI -- up --targets mysql-8.4,mariadb-11.8
```

The CLI pulls missing images, creates containers, waits for readiness, provisions the test users, and writes runtime state to this repo-root path:

```text
artifacts/testdata/testinfra-state.json
```

Runtime state describes the infrastructure that is actually running. Server-backed `up`, `wait`, and `run` commands refresh it from the running containers plus the local SQLite targets; a one-target test run should not leave later runs thinking only that target exists.

The default ports are:

| Target | Port |
| --- | ---: |
| MySQL 8.4 | 13307 |
| MariaDB 11.8 | 13308 |
| MariaDB 11.4 | 13309 |
| MariaDB 10.11 | 13310 |

Check running containers directly when needed:

```powershell
podman ps
```

## Run Tests

Fast local SQLite-only run:

```powershell
dotnet run --project DataLinq.Testing.CLI -- run --alias quick
```

Normal contributor run:

```powershell
dotnet run --project DataLinq.Testing.CLI -- run --alias latest --batch-size 4
```

Full provider matrix:

```powershell
dotnet run --project DataLinq.Testing.CLI -- run --alias all --batch-size 4
```

Run specific suites:

```powershell
dotnet run --project DataLinq.Testing.CLI -- run --suite generators
dotnet run --project DataLinq.Testing.CLI -- run --suite unit
dotnet run --project DataLinq.Testing.CLI -- run --suite compliance --alias latest --batch-size 4
dotnet run --project DataLinq.Testing.CLI -- run --suite mysql --alias latest --batch-size 4
```

Use failure-focused output while iterating:

```powershell
dotnet run --project DataLinq.Testing.CLI -- run --alias latest --output failures
```

On a fresh checkout, do not add `--no-build` to the outer `dotnet run` command. Let the CLI and `dotnet run` build what is missing. Use `--no-build` only after the CLI project has already been built:

```powershell
dotnet run --no-build --project DataLinq.Testing.CLI -c Debug --framework net10.0 -- run --alias latest --batch-size 4
```

## Stop or Reset Containers

Stop the test containers but keep them around:

```powershell
dotnet run --project DataLinq.Testing.CLI -- down
```

Remove them:

```powershell
dotnet run --project DataLinq.Testing.CLI -- down --remove
```

Recreate selected targets from scratch:

```powershell
dotnet run --project DataLinq.Testing.CLI -- reset --targets mysql-8.4
```

## Common Problems

### Podman Is Installed but Tests Cannot Create Containers

Check the machine first:

```powershell
podman machine list
podman info
```

If the machine is stopped:

```powershell
podman machine start
```

Then rerun:

```powershell
dotnet run --project DataLinq.Testing.CLI -- up --alias latest
```

### Missing Test Executable Path

If you see an error like this:

```text
The system cannot find the file specified.
...\bin\Debug\net10.0\DataLinq.Tests.Unit.exe
```

the test project was not built for that configuration. Run without outer `--no-build`, or force a build:

```powershell
dotnet run --project DataLinq.Testing.CLI -- run --alias latest --build
```

### Visual Studio Reports Missing wasm-tools

The full solution includes `src\DataLinq.BlazorWasm`, which requires the .NET WebAssembly build workload. If Visual Studio fails with `NETSDK1147` and says `wasm-tools` must be installed, verify the active command-line SDK first:

```powershell
dotnet --info
dotnet workload list
dotnet workload restore
dotnet build .\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj -c Debug --no-restore
```

If that command-line build succeeds, fully close and reopen Visual Studio. Visual Studio can hold stale workload resolver state after workload restore. If it still fails after restart, use Visual Studio Installer and make sure the ASP.NET/web workload includes the .NET WebAssembly build tools.

The `WASM0001` warnings from `SQLitePCLRaw.provider.e_sqlite3` are separate from this setup problem. They are real WebAssembly compatibility warnings and should not be suppressed as an environment fix.

### Port Already in Use

The server-backed targets use ports 13307 through 13310. If one is already occupied, stop the conflicting process or override the target matrix deliberately. Do not run random local MySQL services on those ports and expect the tests to be meaningful. These ports intentionally avoid the common local MySQL/MariaDB range around 3306 through 3310.

### Runtime State Looks Wrong

The test harness reads:

```text
artifacts/testdata/testinfra-state.json
```

Refresh it by rerunning:

```powershell
dotnet run --project DataLinq.Testing.CLI -- up --alias latest
```

The refreshed state is based on containers that are actually running, not only the alias in the command. If it lists extra server targets, stop or remove those containers explicitly.

or remove and recreate containers:

```powershell
dotnet run --project DataLinq.Testing.CLI -- down --remove
dotnet run --project DataLinq.Testing.CLI -- up --alias latest
```

## Practical First Run

For a new Windows contributor, this is the compact version:

```powershell
git clone https://github.com/bazer/DataLinq.git
cd DataLinq/src

dotnet --info
dotnet workload restore
podman machine init # first time only; skip if the default machine already exists
podman machine start
podman info

dotnet run --project DataLinq.Dev.CLI -- doctor --profile repo
dotnet run --project DataLinq.Dev.CLI -- restore
dotnet run --project DataLinq.Dev.CLI -- build

dotnet run --project DataLinq.Testing.CLI -- up --alias latest
dotnet run --project DataLinq.Testing.CLI -- run --alias latest --batch-size 4 --output failures
```

Use `--alias quick` when you do not need containers. Use `--alias all` before broad provider changes.
