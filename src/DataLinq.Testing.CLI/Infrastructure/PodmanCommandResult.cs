namespace DataLinq.Testing.CLI;

internal sealed record PodmanCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public void ThrowIfFailed(string message)
    {
        if (ExitCode == 0)
            return;

        var detail = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
        throw new System.InvalidOperationException($"{message}{System.Environment.NewLine}{detail}".TrimEnd());
    }
}
