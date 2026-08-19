using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;

namespace DataLinq.Benchmark;

[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class TypedIdExactTerminalBenchmarks : IDisposable
{
    private const string AllocationStagesCategory = "allocation-stages";
    private TypedIdExactTerminalBenchmarkContext? context;
    private TypedIdExactTerminalScenario? executedScenario;

    [ParamsSource(nameof(GetProviderNames))]
    public string ProviderName { get; set; } = "sqlite-memory";

    public static IEnumerable<string> GetProviderNames() => EmployeesBenchmarks.GetProviderNames();

    [GlobalSetup]
    public void GlobalSetup()
    {
        context = new TypedIdExactTerminalBenchmarkContext(
            EmployeesBenchmarks.ResolveProvider(ProviderName));
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (context is not null && executedScenario.HasValue)
        {
            var delta = context.CaptureTelemetryDelta(executedScenario.Value, ProviderName);
            BenchmarkTelemetryDeltaWriter.TryWrite(delta);
        }

        context?.Dispose();
        context = null;
        executedScenario = null;
    }

    [IterationSetup(Target = nameof(ColdTypedIdExactTerminal))]
    public void SetupColdTypedIdExactTerminal() => context!.PrepareCold();

    [BenchmarkCategory(AllocationStagesCategory)]
    [Benchmark(
        OperationsPerInvoke = TypedIdExactTerminalBenchmarkContext.OperationsPerInvoke,
        Description = TypedIdExactTerminalBenchmarkContext.ColdDisplayName)]
    public int ColdTypedIdExactTerminal()
    {
        executedScenario = TypedIdExactTerminalScenario.Cold;
        return context!.ExecuteBatch();
    }

    [IterationSetup(Target = nameof(WarmTypedIdExactTerminal))]
    public void SetupWarmTypedIdExactTerminal() => context!.PrepareWarm();

    [BenchmarkCategory(AllocationStagesCategory)]
    [Benchmark(
        OperationsPerInvoke = TypedIdExactTerminalBenchmarkContext.OperationsPerInvoke,
        Description = TypedIdExactTerminalBenchmarkContext.WarmDisplayName)]
    public int WarmTypedIdExactTerminal()
    {
        executedScenario = TypedIdExactTerminalScenario.Warm;
        return context!.ExecuteBatch();
    }

    public void Dispose() => GlobalCleanup();
}
