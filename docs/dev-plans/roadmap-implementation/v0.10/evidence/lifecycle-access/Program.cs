using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Usage: benchmark.dll fresh-output.json");
var path = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Refusing to overwrite evidence");
var resolver = new AssemblyDependencyResolver(path);
AssemblyLoadContext.Default.Resolving += (context, name) => resolver.ResolveAssemblyToPath(name) is { } dependency ? context.LoadFromAssemblyPath(dependency) : null;
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => resolver.ResolveUnmanagedDllToPath(name) is { } dependency ? NativeLibrary.Load(dependency) : IntPtr.Zero;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
var type = assembly.GetType("DataLinq.Benchmark.AllocationRegressionBenchmarks", throwOnError: true)!;
var rows = new List<object>();
foreach (var providerName in new[] { "sqlite-file", "sqlite-memory" })
{
    var benchmark = Activator.CreateInstance(type)!;
    type.GetProperty("ProviderName")!.SetValue(benchmark, providerName);
    var setup = type.GetMethod("GlobalSetup")!.CreateDelegate<Action>(benchmark);
    var cleanup = type.GetMethod("GlobalCleanup")!.CreateDelegate<Action>(benchmark);
    setup();
    Func<object> getter;
    object expected;
    long allocated;
    const int calls = 10000;
    try
    {
        var context = type.GetField("context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(benchmark)!;
        var database = context.GetType().GetProperty("Database", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!;
        var provider = database.GetType().GetProperty("Provider")!.GetValue(database)!;
        getter = provider.GetType().GetProperty("DatabaseAccess")!.GetMethod!.CreateDelegate<Func<object>>(provider);
        expected = getter();
        _ = Measure(getter, expected, calls);
        allocated = Measure(getter, expected, calls);
    }
    finally { cleanup(); }
    var disposedRejected = false;
    try { _ = getter(); }
    catch (ObjectDisposedException) { disposedRejected = true; }
    if (!disposedRejected) throw new InvalidDataException("Disposed provider still exposes database access");
    rows.Add(new { Provider = providerName, Calls = calls, AllocatedBytes = allocated,
        BytesPerAccess = allocated / (double)calls, StableAccessIdentity = true, DisposedProviderRejected = disposedRejected });
}
var identities = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name!.StartsWith("DataLinq", StringComparison.Ordinal))
    .Select(a => new { Name = a.GetName().Name, Path = a.Location, Sha256 = Hash(a.Location),
        Version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        BuildState = a.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(v => v.Key == "DataLinqRepositoryBuildState")?.Value })
    .OrderBy(a => a.Name).ToArray();
File.WriteAllText(output, JsonSerializer.Serialize(new { Runtime = RuntimeInformation.FrameworkDescription,
    Evidence = "Supplemental synchronous allocation observation of the actual public provider access getter; not canonical timing or a release-valid history",
    ProbeAssemblySha256 = Hash(Assembly.GetExecutingAssembly().Location), Identities = identities, Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(rows));

static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
static long Measure(Func<object> getter, object expected, int calls)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < calls; i++)
        if (!ReferenceEquals(getter(), expected)) throw new InvalidDataException("Database access identity changed");
    return GC.GetAllocatedBytesForCurrentThread() - before;
}
