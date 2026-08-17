namespace DataLinq.DevTools;

internal interface IRepositoryStateCapture
{
    TestRunSummaryRepositoryState Capture(string repositoryRoot);
}

internal sealed class GitRepositoryStateCapture : IRepositoryStateCapture
{
    public static GitRepositoryStateCapture Instance { get; } = new();

    private GitRepositoryStateCapture()
    {
    }

    public TestRunSummaryRepositoryState Capture(string repositoryRoot) =>
        TestRunSummaryReporter.CaptureRepositoryState(repositoryRoot);
}
