using System;

namespace DataLinq.DevTools;

public sealed record ExternalCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public TimeSpan Duration { get; init; }
}
