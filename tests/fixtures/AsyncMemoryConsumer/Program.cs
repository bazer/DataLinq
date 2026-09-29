using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Memory;
using DataLinq.PackageConsumer;

var database = new MemoryDatabase<PackageConsumerDatabase>();
database.Seed<PackageConsumerRow>([new MutablePackageConsumerRow { Id = 17, GroupId = 7, Name = "memory", ExternalGuid = Guid.Empty }]);
if ((await database.FindAsync<PackageConsumerRow>(17))?.Name != "memory") throw new Exception("Memory lookup failed.");
if (await database.Query().Rows.Select(row => row.Id).SingleAsync() != 17) throw new Exception("Memory scalar query failed.");
if ((await database.Query().Rows.ToListAsync()).Count != 1) throw new Exception("Memory entity query failed.");
try { _ = await database.Query().Rows.SumAsync(row => row.Id); throw new Exception("Memory aggregate was accepted."); }
catch (DataLinq.Exceptions.QueryBackendCapabilityException) { }
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try { _ = await database.FindAsync<PackageConsumerRow>(17, cancellation.Token); throw new Exception("Memory cancellation was ignored."); }
catch (OperationCanceledException) { }
var graph = new ImmutableRelationMock<PackageConsumerRow>([]);
if (await graph.CountAsync() != 0) throw new Exception("Standalone graph helper failed.");
Console.WriteLine("Memory-only async package consumer passed on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
