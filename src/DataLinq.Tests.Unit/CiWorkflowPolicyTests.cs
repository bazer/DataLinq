using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class CiWorkflowPolicyTests
{
    [Test]
    public async Task LatestWorkflow_SeparatesSmokeAndParallelProviderLanes()
    {
        var root = RepositoryRootLocator.Find();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "latest.yml"));
        var action = File.ReadAllText(Path.Combine(root, ".github", "actions", "run-test-shard", "action.yml"));

        await Assert.That(workflow).Contains("cancel-in-progress: true");
        await Assert.That(workflow).Contains("Smoke (no Podman)");
        await Assert.That(workflow).Contains("fail-fast: false");
        await Assert.That(workflow).Contains("sqlite-file").And.Contains("sqlite-memory");
        await Assert.That(workflow).Contains("mysql-9.7").And.Contains("mariadb-12.3");
        await Assert.That(workflow).DoesNotContain("mysql-8.4").And.DoesNotContain("mariadb-11.8");
        await Assert.That(action).Contains("--build").And.Contains("dotnet exec");
        await Assert.That(action).Contains("if: always()");
        await Assert.That(action).Contains("github.run_id").And.Contains("github.run_attempt");
    }

    [Test]
    public async Task FullWorkflow_ContainsEveryCanonicalShardAndFailClosedAggregate()
    {
        var root = RepositoryRootLocator.Find();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "full-matrix.yml"));

        foreach (var contract in TestShardEvidenceAggregator.FullMatrixContract)
        {
            var name = contract.TargetId is null
                ? contract.Suite
                : $"{contract.Suite}-{contract.TargetId}";
            await Assert.That(workflow).Contains($"name: {name}");
        }

        await Assert.That(workflow).Contains("fail-fast: false");
        await Assert.That(workflow).Contains("merge-multiple: false");
        await Assert.That(workflow).Contains("aggregate").And.Contains("--commit-sha \"${{ github.sha }}\"");
        await Assert.That(workflow)
            .Contains("Load previous successful matrix baseline")
            .And.Contains("--baseline artifacts/ci/full-matrix-baseline.json")
            .And.Contains("aggregate[\"CaseCountBaseline\"]")
            .And.Contains(".github/badges/full-matrix-baseline.json");
        await Assert.That(workflow).DoesNotContain("--plan full");
        await Assert.That(TestShardEvidenceAggregator.FullMatrixContract.Select(
            static contract => $"{contract.Suite}:{contract.TargetId ?? "-"}").Distinct()).Count().IsEqualTo(17);
    }
}
