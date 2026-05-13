using System;
using System.Threading.Tasks;
using DataLinq.Cache;

namespace DataLinq.Tests.Unit.Core;

public class CacheMemoryPressureTests
{
    [Test]
    public async Task Evaluator_DisabledPolicy_DoesNotClean()
    {
        var evaluator = new CacheCleanupPolicyEvaluator();
        var decision = evaluator.EvaluateMemoryPressure(
            HighPressure(),
            new CacheMemoryEstimate(RowPayloadBytes: 32L * 1024L * 1024L),
            CacheMemoryPressureCleanupPolicy.Disabled,
            DateTimeOffset.UtcNow,
            lastPressureCleanupAt: null);

        await Assert.That(decision.ShouldClean).IsFalse();
        await Assert.That(decision.NoopReason).IsEqualTo("disabled");
    }

    [Test]
    public async Task Evaluator_UnsupportedPressureReader_DoesNotClean()
    {
        var evaluator = new CacheCleanupPolicyEvaluator();
        var decision = evaluator.EvaluateMemoryPressure(
            MemoryPressureSnapshot.Unsupported,
            new CacheMemoryEstimate(RowPayloadBytes: 32L * 1024L * 1024L),
            CacheMemoryPressureCleanupPolicy.Conservative,
            DateTimeOffset.UtcNow,
            lastPressureCleanupAt: null);

        await Assert.That(decision.ShouldClean).IsFalse();
        await Assert.That(decision.NoopReason).IsEqualTo("unsupported");
    }

    [Test]
    public async Task Evaluator_LowPressure_DoesNotClean()
    {
        var evaluator = new CacheCleanupPolicyEvaluator();
        var policy = CacheMemoryPressureCleanupPolicy.Conservative with
        {
            HighMemoryLoadThresholdPercent = 80,
            MinimumCacheBytes = 1
        };
        var decision = evaluator.EvaluateMemoryPressure(
            new MemoryPressureSnapshot(
                IsSupported: true,
                MemoryLoadBytes: 40,
                HighMemoryLoadThresholdBytes: 100,
                TotalAvailableMemoryBytes: 100,
                TotalManagedMemoryBytes: 10),
            new CacheMemoryEstimate(RowPayloadBytes: 32L * 1024L * 1024L),
            policy,
            DateTimeOffset.UtcNow,
            lastPressureCleanupAt: null);

        await Assert.That(decision.ShouldClean).IsFalse();
        await Assert.That(decision.NoopReason).IsEqualTo("below_threshold");
    }

    [Test]
    public async Task Evaluator_HighPressure_ReturnsBoundedCleanupDecision()
    {
        var evaluator = new CacheCleanupPolicyEvaluator();
        var now = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
        var policy = CacheMemoryPressureCleanupPolicy.Conservative with
        {
            HighMemoryLoadThresholdPercent = 75,
            MinimumCacheBytes = 1,
            TargetReductionPercent = 50,
            MaxBytesPerPass = 10,
            MaxRowsPerPass = 3
        };
        var decision = evaluator.EvaluateMemoryPressure(
            HighPressure(),
            new CacheMemoryEstimate(RowPayloadBytes: 100),
            policy,
            now,
            lastPressureCleanupAt: null);

        await Assert.That(decision.ShouldClean).IsTrue();
        await Assert.That(decision.TargetEstimatedCacheBytes).IsEqualTo(90);
        await Assert.That(decision.MaxRowsToRemove).IsEqualTo(3);
    }

    [Test]
    public async Task Evaluator_SustainedHighPressure_RespectsCooldown()
    {
        var evaluator = new CacheCleanupPolicyEvaluator();
        var now = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
        var policy = CacheMemoryPressureCleanupPolicy.Conservative with
        {
            MinimumCacheBytes = 1,
            Cooldown = TimeSpan.FromMinutes(5)
        };
        var decision = evaluator.EvaluateMemoryPressure(
            HighPressure(),
            new CacheMemoryEstimate(RowPayloadBytes: 100),
            policy,
            now,
            lastPressureCleanupAt: now.Subtract(TimeSpan.FromMinutes(1)));

        await Assert.That(decision.ShouldClean).IsFalse();
        await Assert.That(decision.NoopReason).IsEqualTo("cooldown");
    }

    private static MemoryPressureSnapshot HighPressure() => new(
        IsSupported: true,
        MemoryLoadBytes: 95,
        HighMemoryLoadThresholdBytes: 100,
        TotalAvailableMemoryBytes: 100,
        TotalManagedMemoryBytes: 10);
}
