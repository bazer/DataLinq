using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Usage: benchmark.dll expected-commit fresh-output.json");
var path = Path.GetFullPath(args[0]);
var expectedCommit = args[1];
var output = Path.GetFullPath(args[2]);
if (File.Exists(output)) throw new IOException("Refusing to overwrite evidence");
var resolver = new AssemblyDependencyResolver(path);
AssemblyLoadContext.Default.Resolving += (context, name) => resolver.ResolveAssemblyToPath(name) is { } dependency ? context.LoadFromAssemblyPath(dependency) : null;
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => resolver.ResolveUnmanagedDllToPath(name) is { } dependency ? NativeLibrary.Load(dependency) : IntPtr.Zero;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
var contextType = assembly.GetType("DataLinq.Benchmark.BinaryOwnershipBenchmarkContext", throwOnError: true)!;
var rows = new List<object>();
foreach (var size in new[] { 32, 4096, 65536 })
{
    using var fixture = (IDisposable)Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, args: ["memory", size], culture: null)!;
    var row = contextType.GetField("modelRow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture)!;
    var template = contextType.GetField("immutable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture)!;
    var source = template.GetType().GetMethod("GetReadSource")!.Invoke(template, null);
    var table = row.GetType().GetProperty("Table")!.GetValue(row)!;
    var model = table.GetType().GetProperty("Model")!.GetValue(table)!;
    var factory = (Delegate)model.GetType().GetProperty("ReadSourceImmutableFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
    var generated = factory.DynamicInvoke(row, source)!;
    var getter = generated.GetType().GetProperty("Id")!.GetMethod!.CreateDelegate<Func<byte[]>>(generated);
    var first = getter();
    var original = first[0];
    first[0] ^= 0x7f;
    var second = getter();
    var sameReference = ReferenceEquals(first, second);
    var isolatedMutation = second[0] == original;
    first[0] = original;
    const int calls = 10000;
    Measure(getter, calls); // Same synchronous helper for warmup and measurement.
    var measured = Measure(getter, calls);
    if (measured.Checksum != (long)calls * size) throw new InvalidDataException("Unexpected getter result");
    rows.Add(new { PayloadBytes = size, Calls = calls, measured.AllocatedBytes,
        BytesPerCall = measured.AllocatedBytes / (double)calls, measured.Checksum,
        ReusesPublicArray = sameReference, CallerMutationIsolated = isolatedMutation,
        GeneratedType = generated.GetType().FullName });
}
var identities = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("DataLinq", StringComparison.Ordinal))
    .Select(a => new { Name = a.GetName().Name, Path = a.Location, Sha256 = Hash(a.Location),
        Version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        BuildState = a.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(v => v.Key == "DataLinqRepositoryBuildState")?.Value })
    .OrderBy(a => a.Name).ToArray();
// The benchmark provenance target stamps the harness; public library projects do
// not emit this build-state attribute. Require their actual source commit and
// retain every DLL hash instead of inventing a clean flag for those libraries.
if (identities.Any(a => a.Version is null || !a.Version.EndsWith(expectedCommit, StringComparison.Ordinal)) ||
    identities.Single(a => a.Name == "DataLinq.Benchmark").BuildState != "clean")
{
    Console.WriteLine(JsonSerializer.Serialize(identities));
    Console.Error.WriteLine("Unexpected or dirty DataLinq assembly identity");
    Environment.ExitCode = 2;
    return;
}
File.WriteAllText(output, JsonSerializer.Serialize(new { ExpectedCommit = expectedCommit,
    Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    Evidence = "Supplemental thread-allocation probe of actual generated getter; not a canonical latency benchmark or release-valid history",
    ProbeAssemblySha256 = Hash(Assembly.GetExecutingAssembly().Location), Identities = identities, Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(rows));

static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
static (long AllocatedBytes, long Checksum) Measure(Func<byte[]> getter, int calls)
{
    byte[]? last = null;
    long checksum = 0;
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < calls; i++) { last = getter(); checksum += last.Length; }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    GC.KeepAlive(last);
    return (allocated, checksum);
}
