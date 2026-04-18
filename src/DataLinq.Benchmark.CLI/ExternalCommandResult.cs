namespace DataLinq.Benchmark.CLI;

internal sealed record ExternalCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
