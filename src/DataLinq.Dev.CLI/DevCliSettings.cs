using DataLinq.DevTools;

namespace DataLinq.Dev.CLI;

internal sealed record DevCliSettings(
    string RepositoryRoot,
    DevToolPaths Paths)
{
    public static DevCliSettings FromAppContext()
    {
        var repositoryRoot = RepositoryRootLocator.Find();
        return new DevCliSettings(repositoryRoot, DevToolPaths.Create(repositoryRoot));
    }
}
