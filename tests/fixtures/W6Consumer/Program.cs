using DataLinq.Extensions.DependencyInjection;
using DataLinq.SQLite.Extensions.DependencyInjection;
using DataLinq.Testing;
using Microsoft.Extensions.DependencyInjection;
using PackedGenerated;

await using var fixture = await SqliteTestDatabase<AdvancedDatabase>.CreateAsync();
var services = new ServiceCollection();
services.AddDataLinq<AdvancedDatabase>("application").UseSQLiteConnectionString("unused").AsDefault();
services.ReplaceDataLinqWithSqliteInMemory(fixture);
var parent = fixture.Seed(session => session.Insert(new MutableParent { Id = 7 }));
if (parent.Children.Any()) throw new InvalidOperationException("A new parent already has children.");
var child = await fixture.SeedAsync((session, ct) => session.InsertAsync(new MutableChild
{
    Id = new ChildId(17), Tenant = 1, Name = "committed", ParentId = parent.Id
}, ct));
var observed = fixture.UnitOfWorkFactory.Execute(session => session.Query().Children.Single());
if (observed.Id != child.Id || observed.Name != "committed" || parent.Children.Single().Id != child.Id ||
    child.OptionalParent is not null || child.Parent.Id != parent.Id)
    throw new InvalidOperationException("Committed seed, converted composite key or relationship lookup failed.");
fixture.Database.Provider.State.ClearCache();
if ((await Child.GetAsync(1, new ChildId(17), fixture.Database))?.Name != "committed")
    throw new InvalidOperationException("Composite key lookup failed after clearing the cache.");
if (AppContext.TargetFrameworkName != $".NETCoreApp,Version=v{Environment.Version.Major}.0")
    throw new InvalidOperationException("Consumer ran on an unexpected major runtime.");
Console.WriteLine($"W6 consumer passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
