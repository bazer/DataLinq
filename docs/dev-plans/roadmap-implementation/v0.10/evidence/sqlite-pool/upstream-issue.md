Concurrent opens of distinct Microsoft.Data.Sqlite connections can receive the same live native handle. Beginning independent transactions on those two connections then throws `SQLite Error 1: cannot start a transaction within a transaction`.

This was reproduced with the published Microsoft.Data.Sqlite 10.0.11 package, without EF Core, DataLinq, reflection, forced GC, shared connection operations, or concurrent pool clearing. All connection objects remain strongly referenced and open until their handles are inspected.

### Mechanism

`SqliteConnectionInternal.Activate` currently sets `_active = true` before `_outerConnection.SetTarget(outerConnection)`. Activation runs after `SqliteConnectionPool.GetConnection` releases its lock. A competing checkout can therefore see the intermediate active/ownerless state through `Leaked`, reclaim the connection, and lend the same internal connection to another owner.

The same ordering exists on main at `961b3cb6fe78daf720068a5166218138c6c04f73`.

### Reproduction

Run the following project in an empty directory with `dotnet run -- pool-result.json 30000`. It clears only its own pool between waves, with all previous owners closed, then starts 16 concurrent opens and holds every owner alive. It stops and exits 1 when duplicate ownership is detected. Since this is a race, the failing wave varies and a run can pass.

Repro.csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.11" /><PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" /></ItemGroup>
</Project>
```

Program.cs:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// Public APIs only. All owners remain strongly held and open until each wave
// has finished. A duplicate is then checked with two independent read-only transactions.
// No writes, provider internals or forced GC.
var rounds = int.Parse(args[1]);
const int workers = 16;
ThreadPool.SetMinThreads(workers + 2, workers + 2);
var errors = new ConcurrentQueue<string>();
var duplicates = new List<object>();
var transactionErrors = new List<string>();
var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "pool-public-probe.db");
var connectionString = new SqliteConnectionStringBuilder { DataSource = file, Pooling = true }.ToString();
using var control = new SqliteConnection(connectionString);
control.Open();
control.Close();
var stopwatch = Stopwatch.StartNew();
var completed = 0;
for (int round = 0; round < rounds; round++)
{
    // Clear this probe's pool only, with all preceding owners already closed.
    SqliteConnection.ClearPool(control);
    using var ready = new Barrier(workers);
    var connections = Enumerable.Range(0, workers).Select(_ => new SqliteConnection(connectionString)).ToArray();
    try
    {
        await Task.WhenAll(connections.Select(connection => Task.Run(() =>
        {
            ready.SignalAndWait();
            try { connection.Open(); }
            catch (Exception exception) { errors.Enqueue(exception.ToString()); }
        })));
        var handles = connections.Select((connection, owner) => new {
            Owner = owner, State = connection.State, Handle = connection.Handle?.DangerousGetHandle().ToInt64()
        }).ToArray();
        foreach (var owner in handles.Where(x => x.State != System.Data.ConnectionState.Open || !x.Handle.HasValue || x.Handle.Value == 0))
            errors.Enqueue($"Round {round}, owner {owner.Owner}: expected an open live connection; got {owner.State}, handle {owner.Handle}.");
        foreach (var group in handles.Where(x => x.Handle.HasValue).GroupBy(x => x.Handle).Where(g => g.Count() > 1))
        {
            var owners = group.Select(x => x.Owner).ToArray();
            duplicates.Add(new { Round = round, Handle = group.Key, Owners = owners });
            using var firstTransaction = connections[owners[0]].BeginTransaction(System.Data.IsolationLevel.Serializable, deferred: true);
            try
            {
                using var secondTransaction = connections[owners[1]].BeginTransaction(System.Data.IsolationLevel.Serializable, deferred: true);
                secondTransaction.Rollback();
            }
            catch (Microsoft.Data.Sqlite.SqliteException exception)
            {
                transactionErrors.Add(exception.Message);
            }
            firstTransaction.Rollback();
        }
        completed++;
    }
    finally
    {
        foreach (var connection in connections)
            try { connection.Dispose(); }
            catch (Exception exception) { errors.Enqueue(exception.ToString()); }
    }
    if (duplicates.Count != 0 || !errors.IsEmpty)
        break;
    if (completed % 100 == 0)
        Console.WriteLine($"Completed {completed}/{rounds} waves ({workers} concurrent opens each).");
}
SqliteConnection.ClearPool(control);
var assembly = typeof(SqliteConnection).Assembly;
var report = new {
    Kind = "Published Microsoft.Data.Sqlite pool and transaction reproduction",
    Runtime = RuntimeInformation.FrameworkDescription,
    Assembly = assembly.FullName,
    AssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
    RequestedRounds = rounds, CompletedRounds = completed, WorkersPerRound = workers,
    DurationSeconds = stopwatch.Elapsed.TotalSeconds,
    DuplicateLiveNativeHandles = duplicates, SecondBeginErrors = transactionErrors, Errors = errors.ToArray(),
    Limitation = "Public opens and deferred transaction begins only. A passing stress run cannot exclude rare interleavings."
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[0], json);
Console.WriteLine(json);
return duplicates.Count == 0 && errors.IsEmpty ? 0 : 1;
```

One recorded run failed on wave 312 (zero-based index 311), with owners 13 and 15 sharing a native handle. Starting a deferred serializable transaction on each independently reproduced the transaction-within-transaction error. A previous checkout-only run failed on wave 774.

Environment: Windows x64, SDK 10.0.401, .NET runtime 10.0.12, Microsoft.Data.Sqlite 10.0.11, SQLitePCLRaw.bundle_e_sqlite3 3.0.5. The published Microsoft.Data.Sqlite assembly SHA-256 was `4abd9c2a61e580eb853e93ca8953a3cef2c05714ae28d2d1859d4dbc5e5700bc`.

### Proposed correction and validation

I have prepared a minimal change that sets the weak owner before the volatile active flag, plus a regression test in `SqliteConnectionFactoryTest` which checks distinct handles while every owner is still open. This preserves pooling and adds no locks, retries, or public API changes.

- An isolated v10.0.11 source build with a scheduling callback reproduced duplicate ownership and the exact transaction error. Reversing the two writes removed both failures under the same controlled schedule. The callback is diagnostic only and is not in the proposed patch.
- An uninstrumented original source build reproduced duplicate ownership on wave 351; the corrected source build completed 50,000 waves / 800,000 opens without duplicates or errors. These are correctness stress runs, not performance benchmarks.
- The new regression test fails on unmodified main and passes with the correction.
- The corrected upstream project builds with zero warnings/errors using its own toolchain. Its SQLite test application reports 714 passed, 7 existing skips, zero failures (using the repository's normal `category=failing` exclusion). Six skips reference #35585; one references ericsink/SQLitePCL.raw#421. This is the SQLite driver test application, not the full EF Core provider matrix.

I would like to contribute this correction and regression test. Since the issue also affects published 10.0.11, a servicing backport would be useful if the diagnosis is accepted.

### AI disclosure

This fix was found, reproduced and posted with GPT-6 Astra on Extra High, with the repository owner's authorization.
