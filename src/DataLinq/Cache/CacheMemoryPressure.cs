using System;
using DataLinq.Diagnostics;

namespace DataLinq.Cache;

public sealed record CacheMemoryPressureCleanupPolicy
{
    public static CacheMemoryPressureCleanupPolicy Disabled { get; } = new() { Enabled = false };
    public static CacheMemoryPressureCleanupPolicy Conservative { get; } = new() { Enabled = true };

    public bool Enabled { get; init; }
    public int HighMemoryLoadThresholdPercent { get; init; } = 90;
    public long MinimumCacheBytes { get; init; } = 16L * 1024L * 1024L;
    public int TargetReductionPercent { get; init; } = 25;
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxRowsPerPass { get; init; } = 1024;
    public long MaxBytesPerPass { get; init; } = 64L * 1024L * 1024L;

    internal CacheMemoryPressureCleanupPolicy Normalize()
        => this with
        {
            HighMemoryLoadThresholdPercent = Math.Clamp(HighMemoryLoadThresholdPercent, 1, 100),
            MinimumCacheBytes = Math.Max(MinimumCacheBytes, 0),
            TargetReductionPercent = Math.Clamp(TargetReductionPercent, 1, 100),
            Cooldown = Cooldown < TimeSpan.Zero ? TimeSpan.Zero : Cooldown,
            CheckInterval = CheckInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : CheckInterval,
            MaxRowsPerPass = Math.Max(MaxRowsPerPass, 1),
            MaxBytesPerPass = Math.Max(MaxBytesPerPass, 1)
        };
}

internal interface IMemoryPressureReader
{
    MemoryPressureSnapshot GetSnapshot();
}

internal sealed class GcMemoryPressureReader : IMemoryPressureReader
{
    public static GcMemoryPressureReader Instance { get; } = new();

    private GcMemoryPressureReader()
    {
    }

    public MemoryPressureSnapshot GetSnapshot()
    {
        var info = GC.GetGCMemoryInfo();
        return new MemoryPressureSnapshot(
            IsSupported: true,
            MemoryLoadBytes: info.MemoryLoadBytes,
            HighMemoryLoadThresholdBytes: info.HighMemoryLoadThresholdBytes,
            TotalAvailableMemoryBytes: info.TotalAvailableMemoryBytes,
            TotalManagedMemoryBytes: GC.GetTotalMemory(forceFullCollection: false));
    }
}

internal sealed class UnsupportedMemoryPressureReader : IMemoryPressureReader
{
    public static UnsupportedMemoryPressureReader Instance { get; } = new();

    private UnsupportedMemoryPressureReader()
    {
    }

    public MemoryPressureSnapshot GetSnapshot() => MemoryPressureSnapshot.Unsupported;
}

internal readonly record struct MemoryPressureSnapshot(
    bool IsSupported,
    long MemoryLoadBytes,
    long HighMemoryLoadThresholdBytes,
    long TotalAvailableMemoryBytes,
    long TotalManagedMemoryBytes)
{
    public static MemoryPressureSnapshot Unsupported { get; } = new(
        IsSupported: false,
        MemoryLoadBytes: 0,
        HighMemoryLoadThresholdBytes: 0,
        TotalAvailableMemoryBytes: 0,
        TotalManagedMemoryBytes: 0);

    public int MemoryLoadPercent
    {
        get
        {
            if (!IsSupported || HighMemoryLoadThresholdBytes <= 0 || MemoryLoadBytes <= 0)
                return 0;

            var percent = (MemoryLoadBytes * 100d) / HighMemoryLoadThresholdBytes;
            return (int)Math.Clamp(Math.Round(percent, MidpointRounding.AwayFromZero), 0, 100);
        }
    }
}

internal readonly record struct CacheCleanupDecision(
    bool ShouldClean,
    string Reason,
    string Trigger,
    string Basis,
    string NoopReason,
    long? TargetEstimatedCacheBytes,
    int MaxRowsToRemove)
{
    public static CacheCleanupDecision Noop(string noopReason) => new(
        ShouldClean: false,
        Reason: CacheMaintenanceReasons.Unknown,
        Trigger: CacheMaintenanceTriggers.MemoryPressure,
        Basis: CacheMaintenanceBases.EstimatedCacheBytes,
        NoopReason: noopReason,
        TargetEstimatedCacheBytes: null,
        MaxRowsToRemove: 0);
}

internal sealed class CacheCleanupPolicyEvaluator
{
    public CacheCleanupDecision EvaluateMemoryPressure(
        MemoryPressureSnapshot pressure,
        CacheMemoryEstimate cacheEstimate,
        CacheMemoryPressureCleanupPolicy policy,
        DateTimeOffset now,
        DateTimeOffset? lastPressureCleanupAt)
    {
        policy = policy.Normalize();

        if (!policy.Enabled)
            return CacheCleanupDecision.Noop("disabled");

        if (!pressure.IsSupported)
            return CacheCleanupDecision.Noop("unsupported");

        if (pressure.MemoryLoadPercent < policy.HighMemoryLoadThresholdPercent)
            return CacheCleanupDecision.Noop("below_threshold");

        var estimatedCacheBytes = cacheEstimate.EstimatedCacheBytes;
        if (estimatedCacheBytes < policy.MinimumCacheBytes)
            return CacheCleanupDecision.Noop("below_minimum_cache_bytes");

        if (lastPressureCleanupAt.HasValue && now - lastPressureCleanupAt.Value < policy.Cooldown)
            return CacheCleanupDecision.Noop("cooldown");

        var proportionalReduction = CacheMemoryEstimator.SaturatingMultiply(
            estimatedCacheBytes,
            policy.TargetReductionPercent) / 100;
        var reductionBytes = Math.Min(policy.MaxBytesPerPass, Math.Max(proportionalReduction, 1));
        var targetBytes = Math.Max(0, estimatedCacheBytes - reductionBytes);

        return new CacheCleanupDecision(
            ShouldClean: true,
            Reason: CacheMaintenanceReasons.MemoryPressure,
            Trigger: CacheMaintenanceTriggers.MemoryPressure,
            Basis: CacheMaintenanceBases.EstimatedCacheBytes,
            NoopReason: string.Empty,
            TargetEstimatedCacheBytes: targetBytes,
            MaxRowsToRemove: policy.MaxRowsPerPass);
    }
}
