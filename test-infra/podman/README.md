# Test Infrastructure

`DataLinq.Testing.CLI` is the canonical entry point for local test infrastructure orchestration.

The full documentation now lives in:

- [`docs/contributing/DataLinq.Testing.CLI.md`](../../docs/contributing/DataLinq.Testing.CLI.md)
- [`docs/contributing/Internal Tooling.md`](../../docs/contributing/Internal%20Tooling.md)

Keep this README as a quick local pointer, not as a second full copy of the tool documentation.

## Quick Start

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- list
dotnet run --project src/DataLinq.Testing.CLI -- up --alias latest
dotnet run --project src/DataLinq.Testing.CLI -- run --suite all --alias latest --batch-size 4
dotnet run --project src/DataLinq.Testing.CLI -- down
```

The target matrix still lives in `test-infra/podman/matrix.json`.
