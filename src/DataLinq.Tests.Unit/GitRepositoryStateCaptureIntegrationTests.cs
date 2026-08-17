using System.Linq;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class GitRepositoryStateCaptureIntegrationTests
{
    [Test]
    [Property("Purpose", "ToolingIntegration")]
    [Property("Resource", "GitProcess")]
    public async Task Capture_RealRepositoryReturnsGitIdentity()
    {
        var state = GitRepositoryStateCapture.Instance.Capture(RepositoryRootLocator.Find());

        await Assert.That(state.Captured).IsTrue();
        await Assert.That(state.Commit.Length).IsEqualTo(40);
        await Assert.That(state.Commit.All(static character => char.IsAsciiHexDigit(character))).IsTrue();
        await Assert.That(state.Branch).IsNotEqualTo("unknown");
        await Assert.That(state.StatusSha256.Length).IsEqualTo(64);
    }
}
