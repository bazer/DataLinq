using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Memory;
using DataLinq.Mutation;

namespace DataLinq.Tests.Memory;

public sealed class MemoryOrderedInt32ComparisonTests
{
    private static readonly Guid CanonicalGuid =
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Test]
    public async Task RelationalOperators_RespectInclusiveBoundsExtremesAndOperandInversion()
    {
        var database = CreatePrimitiveDatabase();
        var rows = database.Query().Rows;
        var zero = 0;
        var minimum = int.MinValue;
        var maximum = int.MaxValue;
        var lowerGroup = 3;
        var upperGroup = 7;

        var keyLess = SortedIds(rows.Where(row => row.Id < zero).ToArray());
        var keyLessOrEqual = SortedIds(rows.Where(row => row.Id <= zero).ToArray());
        var reversedKeyGreater = SortedIds(rows.Where(row => zero > row.Id).ToArray());
        var reversedKeyGreaterOrEqual = SortedIds(rows.Where(row => zero >= row.Id).ToArray());
        var keyGreater = SortedIds(rows.Where(row => row.Id > zero).ToArray());
        var keyGreaterOrEqual = SortedIds(rows.Where(row => row.Id >= zero).ToArray());
        var reversedKeyLess = SortedIds(rows.Where(row => zero < row.Id).ToArray());
        var reversedKeyLessOrEqual = SortedIds(rows.Where(row => zero <= row.Id).ToArray());
        var keyLessThanMaximum = SortedIds(rows.Where(row => row.Id < maximum).ToArray());
        var reversedMaximumGreater = SortedIds(rows.Where(row => maximum > row.Id).ToArray());
        var keyLessOrEqualMinimum = SortedIds(rows.Where(row => row.Id <= minimum).ToArray());
        var reversedMinimumGreaterOrEqual = SortedIds(rows.Where(row => minimum >= row.Id).ToArray());

        await Assert.That(keyLess).IsEqualTo($"{int.MinValue},-11");
        await Assert.That(keyLessOrEqual).IsEqualTo($"{int.MinValue},-11,0");
        await Assert.That(reversedKeyGreater).IsEqualTo(keyLess);
        await Assert.That(reversedKeyGreaterOrEqual).IsEqualTo(keyLessOrEqual);
        await Assert.That(keyGreater).IsEqualTo($"17,{int.MaxValue}");
        await Assert.That(keyGreaterOrEqual).IsEqualTo($"0,17,{int.MaxValue}");
        await Assert.That(reversedKeyLess).IsEqualTo(keyGreater);
        await Assert.That(reversedKeyLessOrEqual).IsEqualTo(keyGreaterOrEqual);
        await Assert.That(keyLessThanMaximum)
            .IsEqualTo($"{int.MinValue},-11,0,17");
        await Assert.That(reversedMaximumGreater).IsEqualTo(keyLessThanMaximum);
        await Assert.That(keyLessOrEqualMinimum).IsEqualTo(int.MinValue.ToString());
        await Assert.That(reversedMinimumGreaterOrEqual).IsEqualTo(keyLessOrEqualMinimum);

        var nonKeyLess = SortedIds(rows.Where(row => row.GroupId < upperGroup).ToArray());
        var nonKeyLessOrEqual = SortedIds(rows.Where(row => row.GroupId <= lowerGroup).ToArray());
        var reversedNonKeyGreater = SortedIds(rows.Where(row => upperGroup > row.GroupId).ToArray());
        var reversedNonKeyGreaterOrEqual = SortedIds(rows.Where(row => lowerGroup >= row.GroupId).ToArray());
        var nonKeyGreater = SortedIds(rows.Where(row => row.GroupId > lowerGroup).ToArray());
        var nonKeyGreaterOrEqual = SortedIds(rows.Where(row => row.GroupId >= upperGroup).ToArray());
        var reversedNonKeyLess = SortedIds(rows.Where(row => lowerGroup < row.GroupId).ToArray());
        var reversedNonKeyLessOrEqual = SortedIds(rows.Where(row => upperGroup <= row.GroupId).ToArray());

        await Assert.That(nonKeyLess).IsEqualTo($"{int.MinValue},0");
        await Assert.That(nonKeyLessOrEqual).IsEqualTo(nonKeyLess);
        await Assert.That(reversedNonKeyGreater).IsEqualTo(nonKeyLess);
        await Assert.That(reversedNonKeyGreaterOrEqual).IsEqualTo(nonKeyLessOrEqual);
        await Assert.That(nonKeyGreater).IsEqualTo($"-11,17,{int.MaxValue}");
        await Assert.That(nonKeyGreaterOrEqual).IsEqualTo(nonKeyGreater);
        await Assert.That(reversedNonKeyLess).IsEqualTo(nonKeyGreater);
        await Assert.That(reversedNonKeyLessOrEqual).IsEqualTo(nonKeyGreaterOrEqual);
    }

    [Test]
    public async Task RelationalPredicate_RebindsCapturedInclusiveBoundaryForEveryEnumeration()
    {
        var database = CreatePrimitiveDatabase();
        var rows = database.Query().Rows;
        var inclusiveMinimum = 0;
        var reboundQuery = rows.Where(row => row.Id >= inclusiveMinimum);

        var fromZero = SortedIds(reboundQuery.ToArray());
        inclusiveMinimum = int.MaxValue;
        var maximumOnly = SortedIds(reboundQuery.ToArray());
        inclusiveMinimum = int.MinValue;
        var allRows = SortedIds(reboundQuery.ToArray());
        var reversed = SortedIds(rows.Where(row => inclusiveMinimum <= row.Id).ToArray());

        await Assert.That(fromZero).IsEqualTo($"0,17,{int.MaxValue}");
        await Assert.That(maximumOnly).IsEqualTo(int.MaxValue.ToString());
        await Assert.That(allRows)
            .IsEqualTo($"{int.MinValue},-11,0,17,{int.MaxValue}");
        await Assert.That(reversed).IsEqualTo(allRows);
    }

    [Test]
    public async Task RelationalPredicates_NestInsideAndOrAndNot()
    {
        var database = CreatePrimitiveDatabase();
        var lowerId = -11;
        var upperId = 17;
        var excludedGroup = 3;

        var selected = database.Query().Rows
            .Where(row =>
                (row.Id >= lowerId && row.Id <= upperId) ||
                !(row.GroupId > excludedGroup))
            .ToArray();

        await Assert.That(SortedIds(selected))
            .IsEqualTo($"{int.MinValue},-11,0,17");
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(1);
    }

    [Test]
    public async Task RelationalPredicates_ComposeWithOrderTakeProjectionAnyAndCountWithoutEntityWork()
    {
        var projectionDatabase = CreatePrimitiveDatabase();
        var lowerId = int.MinValue;
        var upperId = 17;

        var projectedGroups = projectionDatabase.Query().Rows
            .Where(row => row.Id > lowerId && row.Id <= upperId)
            .OrderBy(static row => row.Id)
            .Take(2)
            .Select(static row => row.GroupId)
            .ToArray();

        await Assert.That(string.Join(",", projectedGroups)).IsEqualTo("7,3");
        AssertNoPrimitiveEntityWork(projectionDatabase);

        var anyDatabase = CreatePrimitiveDatabase();
        var any = anyDatabase.Query().Rows.Any(row => row.Id >= int.MaxValue);

        await Assert.That(any).IsTrue();
        AssertNoPrimitiveEntityWork(anyDatabase);

        var countDatabase = CreatePrimitiveDatabase();
        var count = countDatabase.Query().Rows.Count(row =>
            row.GroupId > 3 && row.Id < int.MaxValue);

        await Assert.That(count).IsEqualTo(2);
        AssertNoPrimitiveEntityWork(countDatabase);
    }

    [Test]
    public async Task NearbyNullableWidenedBoxedAndColumnRelationalShapes_RejectBeforeMemoryWork()
    {
        var database = CreatePrimitiveDatabase();
        var before = database.Diagnostics;
        int? nullableBoundary = 3;
        long widenedBoundary = 3;
        object boxedBoundary = 3;

        var nullable = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => (int?)row.GroupId > nullableBoundary)
                .ToArray());
        var widened = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => row.GroupId > widenedBoundary)
                .ToArray());
        var columnToColumn = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => row.Id > row.GroupId)
                .ToArray());
        var boxed = Capture<QueryTranslationException>(() =>
            database.Query().Rows
                .Where(row => ((IComparable)row.GroupId).CompareTo(boxedBoundary) > 0)
                .ToArray());

        foreach (var exception in new[] { nullable, widened, columnToColumn })
        {
            await Assert.That(exception.Feature)
                .IsEqualTo("ComparisonShape:DefaultNullSemantics");
            await Assert.That(exception.Location)
                .IsEqualTo("operations[0].predicate.shape");
        }

        await Assert.That(boxed.Message).Contains("is not supported");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task NullableConverterBackedAndCanonicalGuidRelationalShapes_RejectBeforeConversionOrMemoryWork()
    {
        var database = CreateUnsupportedShapeDatabase();
        MemoryOrderedIntIdConverter.Reset();
        MemoryOrderedGuidIdConverter.Reset();
        var before = database.Diagnostics;
        int? nullableBoundary = 0;
        var convertedBoundary = new MemoryOrderedIntId(0);
        var guidBoundary = new MemoryOrderedGuidId(Guid.Empty);

        var nullable = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => row.OptionalScore >= nullableBoundary)
                .ToArray());
        var converted = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => row.ConvertedScore > convertedBoundary)
                .ToArray());
        var canonicalGuid = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => row.ConvertedGuid < guidBoundary)
                .ToArray());

        foreach (var exception in new[] { nullable, converted, canonicalGuid })
        {
            await Assert.That(exception.Feature)
                .IsEqualTo("ComparisonShape:DefaultNullSemantics");
            await Assert.That(exception.Location)
                .IsEqualTo("operations[0].predicate.shape");
        }

        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(database.GetMaterializedRowCount<MemoryOrderedComparisonRow>())
            .IsEqualTo(0);
        await Assert.That(MemoryOrderedIntIdConverter.ToProviderCalls).IsEqualTo(0);
        await Assert.That(MemoryOrderedIntIdConverter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(MemoryOrderedGuidIdConverter.ToProviderCalls).IsEqualTo(0);
        await Assert.That(MemoryOrderedGuidIdConverter.FromProviderCalls).IsEqualTo(0);
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreatePrimitiveDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        return database.SeedCanonical<MemoryPrimitiveRow>(
            CreatePrimitiveCanonicalRow(database, id: 17, groupId: 7, name: "seventeen"),
            CreatePrimitiveCanonicalRow(database, id: int.MinValue, groupId: 3, name: "minimum"),
            CreatePrimitiveCanonicalRow(database, id: int.MaxValue, groupId: 7, name: "maximum"),
            CreatePrimitiveCanonicalRow(database, id: -11, groupId: 7, name: "negative-eleven"),
            CreatePrimitiveCanonicalRow(database, id: 0, groupId: 3, name: "zero"));
    }

    private static MemoryDatabase<MemoryOrderedComparisonDatabase> CreateUnsupportedShapeDatabase()
    {
        var database = new MemoryDatabase<MemoryOrderedComparisonDatabase>();
        var table = database.Metadata
            .GetTableModel(typeof(MemoryOrderedComparisonRow))
            .Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = 1;
        values[table.GetColumnByDbName("optional_score").Index] = null;
        values[table.GetColumnByDbName("converted_score").Index] = 7;
        values[table.GetColumnByDbName("converted_guid").Index] = CanonicalGuid;
        return database.SeedCanonical<MemoryOrderedComparisonRow>(values);
    }

    private static object?[] CreatePrimitiveCanonicalRow(
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

    private static string SortedIds(MemoryPrimitiveRow[] rows) =>
        string.Join(",", rows.Select(static row => row.Id).Order());

    private static void AssertNoPrimitiveEntityWork(
        MemoryDatabase<MemoryPrimitiveDatabase> database)
    {
        if (database.Diagnostics.CacheLookups != 0 ||
            database.Diagnostics.CacheHits != 0 ||
            database.Diagnostics.CacheMisses != 0 ||
            database.Diagnostics.Materializations != 0 ||
            database.Diagnostics.CacheInsertions != 0 ||
            database.GetMaterializedRowCount<MemoryPrimitiveRow>() != 0)
        {
            throw new Exception(
                "Ordered Int32 scalar execution unexpectedly touched entity materialization or RowCache.");
        }
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

        throw new InvalidOperationException(
            $"Expected exception of type '{typeof(TException).Name}'.");
    }
}

public readonly record struct MemoryOrderedIntId(int Value)
{
    public static bool operator <(MemoryOrderedIntId left, MemoryOrderedIntId right) =>
        left.Value < right.Value;

    public static bool operator <=(MemoryOrderedIntId left, MemoryOrderedIntId right) =>
        left.Value <= right.Value;

    public static bool operator >(MemoryOrderedIntId left, MemoryOrderedIntId right) =>
        left.Value > right.Value;

    public static bool operator >=(MemoryOrderedIntId left, MemoryOrderedIntId right) =>
        left.Value >= right.Value;
}

public sealed class MemoryOrderedIntIdConverter
    : DataLinqScalarConverter<MemoryOrderedIntId, int>
{
    private static int toProviderCalls;
    private static int fromProviderCalls;

    public static int ToProviderCalls => Volatile.Read(ref toProviderCalls);

    public static int FromProviderCalls => Volatile.Read(ref fromProviderCalls);

    public static void Reset()
    {
        Volatile.Write(ref toProviderCalls, 0);
        Volatile.Write(ref fromProviderCalls, 0);
    }

    public override int ToProvider(
        MemoryOrderedIntId modelValue,
        in ScalarConversionContext context)
    {
        Interlocked.Increment(ref toProviderCalls);
        return modelValue.Value;
    }

    public override MemoryOrderedIntId FromProvider(
        int providerValue,
        in ScalarConversionContext context)
    {
        Interlocked.Increment(ref fromProviderCalls);
        return new MemoryOrderedIntId(providerValue);
    }
}

public readonly record struct MemoryOrderedGuidId(Guid Value)
{
    public static bool operator <(MemoryOrderedGuidId left, MemoryOrderedGuidId right) =>
        left.Value.CompareTo(right.Value) < 0;

    public static bool operator <=(MemoryOrderedGuidId left, MemoryOrderedGuidId right) =>
        left.Value.CompareTo(right.Value) <= 0;

    public static bool operator >(MemoryOrderedGuidId left, MemoryOrderedGuidId right) =>
        left.Value.CompareTo(right.Value) > 0;

    public static bool operator >=(MemoryOrderedGuidId left, MemoryOrderedGuidId right) =>
        left.Value.CompareTo(right.Value) >= 0;
}

public sealed class MemoryOrderedGuidIdConverter
    : DataLinqScalarConverter<MemoryOrderedGuidId, Guid>
{
    private static int toProviderCalls;
    private static int fromProviderCalls;

    public static int ToProviderCalls => Volatile.Read(ref toProviderCalls);

    public static int FromProviderCalls => Volatile.Read(ref fromProviderCalls);

    public static void Reset()
    {
        Volatile.Write(ref toProviderCalls, 0);
        Volatile.Write(ref fromProviderCalls, 0);
    }

    public override Guid ToProvider(
        MemoryOrderedGuidId modelValue,
        in ScalarConversionContext context)
    {
        Interlocked.Increment(ref toProviderCalls);
        return modelValue.Value;
    }

    public override MemoryOrderedGuidId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context)
    {
        Interlocked.Increment(ref fromProviderCalls);
        return new MemoryOrderedGuidId(providerValue);
    }
}

[UseCache]
[Database("memory_ordered_comparison_shapes")]
public sealed partial class MemoryOrderedComparisonDatabase(IDataLinqReadSource readSource)
    : IDatabaseModel
{
    public DbRead<MemoryOrderedComparisonRow> Rows { get; } = new(readSource);
}

[Table("memory_ordered_comparison_rows")]
public abstract partial class MemoryOrderedComparisonRow :
    Immutable<MemoryOrderedComparisonRow, MemoryOrderedComparisonDatabase>,
    ITableModel<MemoryOrderedComparisonDatabase>
{
    protected MemoryOrderedComparisonRow(
        IRowData rowData,
        IDataSourceAccess dataSource)
        : base(rowData, dataSource)
    {
    }

    protected MemoryOrderedComparisonRow(
        IRowData rowData,
        IDataLinqReadSource readSource)
        : base(rowData, readSource)
    {
    }

    [PrimaryKey]
    [Column("id")]
    public abstract int Id { get; }

    [Nullable]
    [Column("optional_score")]
    public abstract int? OptionalScore { get; }

    [Column("converted_score")]
    [ScalarConverter(typeof(MemoryOrderedIntIdConverter))]
    public abstract MemoryOrderedIntId ConvertedScore { get; }

    [Column("converted_guid")]
    [ScalarConverter(typeof(MemoryOrderedGuidIdConverter))]
    [Type(DatabaseType.SQLite, "TEXT")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text36)]
    public abstract MemoryOrderedGuidId ConvertedGuid { get; }
}
