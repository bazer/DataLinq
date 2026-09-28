using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;

if (args.Length != 4) throw new ArgumentException("Usage: benchmark.dll Method iterations output-prefix");
var assemblyPath = Path.GetFullPath(args[0]);
var methodName = args[1];
var iterations = int.Parse(args[2]);
var prefix = Path.GetFullPath(args[3]);
if (iterations <= 0 || iterations > 100) throw new ArgumentOutOfRangeException(nameof(iterations));
if (File.Exists(prefix + ".json") || File.Exists(prefix + ".nettrace")) throw new IOException("Output exists.");
var resolver = new AssemblyDependencyResolver(assemblyPath);
AssemblyLoadContext.Default.Resolving += (context, name) => resolver.ResolveAssemblyToPath(name) is { } path
    ? context.LoadFromAssemblyPath(path) : null;
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => resolver.ResolveUnmanagedDllToPath(name) is { } path
    ? NativeLibrary.Load(path) : IntPtr.Zero;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
var benchmarkTypeName = methodName switch
{
    "RepeatedNonPrimaryKeyEqualityFetch" or "RepeatedInPredicateFetch" or "RepeatedScalarAny" or "SqlAdapterScalarAny" => "DataLinq.Benchmark.EmployeesBenchmarks",
    "ColdPrimaryKeyFetch" or "ColdRelationTraversal" or "WarmPrimaryKeyFetch" or "UpdateEmployees" or "CrudWorkflowSmall" or "CrudWorkflowBatch" => "DataLinq.Benchmark.AllocationRegressionBenchmarks",
    _ => throw new ArgumentException("Unsupported audited workload.")
};
var type = assembly.GetType(benchmarkTypeName, throwOnError: true)!;
var benchmark = Activator.CreateInstance(type)!;
type.GetProperty("ProviderName")!.SetValue(benchmark, "sqlite-memory");
Action ActionFor(string name) => type.GetMethod(name) is { } method ? method.CreateDelegate<Action>(benchmark) : () => { };
Action RequiredActionFor(string name) => type.GetMethod(name)?.CreateDelegate<Action>(benchmark)
    ?? throw new MissingMethodException(type.FullName, name);
var globalSetup = RequiredActionFor("GlobalSetup");
var globalCleanup = RequiredActionFor("GlobalCleanup");
var setup = RequiredActionFor("Setup" + methodName);
var cleanup = ActionFor("Cleanup" + methodName);
var work = type.GetMethod(methodName)!.CreateDelegate<Func<int>>(benchmark);
var operationsPerInvoke = methodName switch
{
    "ColdPrimaryKeyFetch" or "ColdRelationTraversal" => 1000,
    "WarmPrimaryKeyFetch" => 60000,
    "UpdateEmployees" => 2000,
    "CrudWorkflowSmall" or "CrudWorkflowBatch" => 350,
    "RepeatedNonPrimaryKeyEqualityFetch" or "RepeatedInPredicateFetch" or "RepeatedScalarAny" => 1000,
    "SqlAdapterScalarAny" => 3000,
    _ => throw new ArgumentException("Unsupported audited workload.")
};
var benchmarkAttribute = type.GetMethod(methodName)!.GetCustomAttributes()
    .Single(value => value.GetType().FullName == "BenchmarkDotNet.Attributes.BenchmarkAttribute");
var declaredOperations = (int)benchmarkAttribute.GetType().GetProperty("OperationsPerInvoke")!.GetValue(benchmarkAttribute)!;
if (declaredOperations != operationsPerInvoke)
    throw new InvalidDataException($"Operation mismatch: declared={declaredOperations}, audited={operationsPerInvoke}.");
globalSetup();
try
{
    var warmupBytes = new List<long>();
    for (var warmup = 0; warmup < 20; warmup++)
    {
        setup();
        var beforeWarmup = GC.GetAllocatedBytesForCurrentThread();
        _ = work();
        warmupBytes.Add(GC.GetAllocatedBytesForCurrentThread() - beforeWarmup);
        cleanup();
    }
    var dependenciesBefore = CaptureDependencies();
    var marks = ProbeMarks.Log;
    var client = new DiagnosticsClient(Environment.ProcessId);
    using var session = client.StartEventPipeSession([
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, (long)ClrTraceEventParser.Keywords.GC),
        new EventPipeProvider("DataLinq-W1-AllocationProbe", EventLevel.Informational)
    ], requestRundown: false);
    using var trace = File.Create(prefix + ".nettrace");
    var copy = Task.Run(async () => await session.EventStream.CopyToAsync(trace));
    var perInvocation = new List<long>();
    var checksum = 0;
    var started = DateTime.UtcNow;
    for (var index = 0; index < iterations; index++)
    {
        setup();
        marks.WorkStart(index);
        var before = GC.GetAllocatedBytesForCurrentThread();
        try { checksum = unchecked(checksum + work()); }
        finally
        {
            perInvocation.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            marks.WorkEnd(index);
        }
        cleanup();
    }
    session.Stop();
    copy.GetAwaiter().GetResult();
    trace.Flush();
    trace.Close();
    var byType = new Dictionary<string, (long Bytes, int Ticks)>();
    var activeThreads = new HashSet<int>();
    var starts = 0;
    var ends = 0;
    using (var events = new EventPipeEventSource(prefix + ".nettrace"))
    {
        events.Dynamic.All += data =>
        {
            if (data.ProviderName != "DataLinq-W1-AllocationProbe") return;
            if ((int)data.ID == 1) { activeThreads.Add(data.ThreadID); starts++; }
            if ((int)data.ID == 2) { activeThreads.Remove(data.ThreadID); ends++; }
        };
        events.Clr.GCAllocationTick += data =>
        {
            if (!activeThreads.Contains(data.ThreadID)) return;
            var name = data.TypeName ?? "<unknown>";
            var current = byType.GetValueOrDefault(name);
            byType[name] = (current.Bytes + (long)data.AllocationAmount64, current.Ticks + 1);
        };
        events.Process();
    }
    if (starts != iterations || ends != iterations || activeThreads.Count != 0 || byType.Count == 0)
        throw new InvalidDataException($"Incomplete capture: starts={starts}, ends={ends}, types={byType.Count}.");
    // Invoke the harness's own telemetry-only replay after the trace has stopped.
    var scenario = type.GetField("executedScenario", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(benchmark)
        ?? throw new InvalidDataException("Benchmark did not select its scenario.");
    object telemetry;
    if (benchmarkTypeName.EndsWith(".AllocationRegressionBenchmarks", StringComparison.Ordinal))
        telemetry = type.GetMethod("CaptureTelemetryDelta", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(benchmark, [scenario, "sqlite-memory"])!;
    else
    {
        var context = type.GetField("context", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(benchmark)!;
        telemetry = context.GetType().GetMethod("CaptureTelemetryDelta")!.Invoke(context, [scenario, "sqlite-memory"])!;
    }
    var telemetryJson = JsonSerializer.SerializeToElement(telemetry, telemetry.GetType());
    var dependenciesAfter = CaptureDependencies();
    foreach (var dependency in dependenciesBefore)
    {
        var after = dependenciesAfter.SingleOrDefault(value => value.Path == dependency.Path);
        if (after is null || after.Sha256 != dependency.Sha256)
            throw new InvalidDataException("A loaded dependency changed during capture: " + dependency.Path);
    }
    var totalOperations = (long)operationsPerInvoke * iterations;
    var result = new
    {
        Purpose = "Diagnostic allocation sampling only; no timing or strict release-evidence claim.",
        Caveat = "GC allocation ticks are sampled type estimates; ticks crossing phase boundaries can include preceding setup allocation. Exact managed bytes use the synchronous workload thread only, exclude setup/cleanup, and are not process-wide allocation.",
        StartedAtUtc = started,
        CompletedAtUtc = DateTime.UtcNow,
        Runtime = RuntimeInformation.FrameworkDescription,
        Assembly = assemblyPath,
        AssemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant(),
        BenchmarkType = benchmarkTypeName, Method = methodName, Provider = "sqlite-memory", Warmups = 20, WarmupInvocationAllocatedBytes = warmupBytes, Iterations = iterations, OperationsPerInvoke = operationsPerInvoke,
        DependenciesBefore = dependenciesBefore, DependenciesAfter = dependenciesAfter,
        ProbeAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))).ToLowerInvariant(),
        TotalOperations = totalOperations, Checksum = checksum,
        TelemetryReplay = telemetryJson,
        InvocationAllocatedBytes = perInvocation,
        WorkloadThreadBytesPerOperation = (double)perInvocation.Sum() / totalOperations,
        SampleTicks = byType.Values.Sum(x => x.Ticks),
        SampledBytes = byType.Values.Sum(x => x.Bytes),
        Types = byType.OrderByDescending(x => x.Value.Bytes).Select(x => new
            { Type = x.Key, SampledBytes = x.Value.Bytes, Ticks = x.Value.Ticks }).ToArray()
    };
    File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{methodName}: {result.WorkloadThreadBytesPerOperation:F2} managed B/op; {result.SampleTicks} sample ticks; {starts}/{ends} markers.");
}
finally { globalCleanup(); }


DependencyIdentity[] CaptureDependencies()
{
    var benchmarkDirectory = Path.GetDirectoryName(assemblyPath)! + Path.DirectorySeparatorChar;
    return AppDomain.CurrentDomain.GetAssemblies()
        .Where(value => !value.IsDynamic && !string.IsNullOrEmpty(value.Location) &&
            value.Location.StartsWith(benchmarkDirectory, StringComparison.OrdinalIgnoreCase))
        .OrderBy(value => value.Location, StringComparer.OrdinalIgnoreCase)
        .Select(value => new DependencyIdentity(value.GetName().Name!, value.Location,
            value.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            value.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value),
            value.ManifestModule.ModuleVersionId,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(value.Location))).ToLowerInvariant()))
        .ToArray();
}

sealed record DependencyIdentity(string Name, string Path, string? InformationalVersion,
    Dictionary<string, string?> Metadata, Guid ModuleVersionId, string Sha256);

[EventSource(Name = "DataLinq-W1-AllocationProbe")]
sealed class ProbeMarks : EventSource
{
    internal static readonly ProbeMarks Log = new();
    [Event(1, Level = EventLevel.Informational)] public void WorkStart(int iteration) => WriteEvent(1, iteration);
    [Event(2, Level = EventLevel.Informational)] public void WorkEnd(int iteration) => WriteEvent(2, iteration);
}
