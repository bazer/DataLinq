namespace DataLinq.Testing.CLI;

internal sealed record ExternalCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
