using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryUnsupportedDiagnosticsTests
{
    [Test]
    public async Task JoinPlan_ReportsMultipleSourcesBeforeStoreWork()
    {
        var database = CreateDatabase();
        var before = database.Diagnostics;

        var exception = Capture<QueryTranslationException>(() =>
            database.Model.Rows
                .Join(
                    database.Model.Rows,
                    static left => left.Id,
                    static right => right.Id,
                    static (left, right) => new
                    {
                        LeftId = left.Id,
                        RightId = right.Id
                    })
                .ToArray());

        await Assert.That(exception.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'SourceCount:Multiple'");
        await Assert.That(exception.Message).Contains("Location: sources");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    public async Task GroupingPlan_ReportsGroupByBeforeStoreWork()
    {
        var database = CreateDatabase();
        var before = database.Diagnostics;

        var exception = Capture<QueryTranslationException>(() =>
            database.Model.Rows
                .GroupBy(static row => row.GroupId)
                .Select(static group => new
                {
                    GroupId = group.Key,
                    Count = group.Count()
                })
                .ToArray());

        await Assert.That(exception.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'Operation:GroupBy'");
        await Assert.That(exception.Message).Contains("Location: operations[0]");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    public async Task RowLocalProjection_ReportsProjectionKindWithoutLeakingCapture()
    {
        var database = CreateDatabase();
        var before = database.Diagnostics;
        var sensitivePrefix = "memory-diagnostic-secret:";

        var exception = Capture<QueryTranslationException>(() =>
            database.Model.Rows
                .Select(row => sensitivePrefix + row.Name)
                .ToArray());

        await Assert.That(exception.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'Projection:ComputedRowLocalExpression'");
        await Assert.That(exception.Message).Contains("Location: projection");
        await Assert.That(exception.Message).DoesNotContain(sensitivePrefix);
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        return database.SeedCanonical<MemoryPrimitiveRow>(
            CreateCanonicalRow(database, id: 17, groupId: 3, name: "seventeen"),
            CreateCanonicalRow(database, id: 42, groupId: 7, name: "forty-two"));
    }

    private static object?[] CreateCanonicalRow(
        MemoryDatabase<MemoryPrimitiveDatabase> database,
        int id,
        int groupId,
        string name)
    {
        var table = database.Metadata.GetTableModel(typeof(MemoryPrimitiveRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = id;
        values[table.GetColumnByDbName("group_id").Index] = groupId;
        values[table.GetColumnByDbName("name").Index] = name;
        return values;
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }
}
