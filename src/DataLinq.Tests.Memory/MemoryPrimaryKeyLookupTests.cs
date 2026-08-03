using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Memory;
using DataLinq.Mutation;
using TUnit.Assertions.Enums;

namespace DataLinq.Tests.Memory;

public sealed class MemoryPrimaryKeyLookupTests
{
    private static readonly Guid KnownId = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid KnownDirectGuid = new("f1e2d3c4-b5a6-4789-90ab-cdef12345678");
    private static readonly Guid KnownRelatedId = new("10213243-5465-7687-98a9-bacbdcedfe0f");

    [Test]
    public async Task PublicFind_PrimitiveHitMissAndWarmLookupUseTheCanonicalIndexAndIdentityCache()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 42, GroupId = 7, Name = "forty-two" }
        ]);

        var cold = database.Find<MemoryPrimitiveRow>(42);
        var warm = database.Find<MemoryPrimitiveRow>(42);
        var missing = database.Find<MemoryPrimitiveRow>(999);

        await Assert.That(cold).IsNotNull();
        await Assert.That(cold!.Id).IsEqualTo(42);
        await Assert.That(cold.Name).IsEqualTo("forty-two");
        await Assert.That(warm).IsSameReferenceAs(cold);
        await Assert.That(missing).IsNull();
        await Assert.That(cold.GetReadSource()).IsSameReferenceAs(database.ReadSource);

        var diagnostics = database.Diagnostics;
        await Assert.That(diagnostics.PrimaryKeyRequests).IsEqualTo(3);
        await Assert.That(diagnostics.PrimaryKeyProbes).IsEqualTo(3);
        await Assert.That(diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(diagnostics.CacheLookups).IsEqualTo(2);
        await Assert.That(diagnostics.CacheHits).IsEqualTo(1);
        await Assert.That(diagnostics.CacheMisses).IsEqualTo(1);
        await Assert.That(diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(diagnostics.CacheInsertions).IsEqualTo(1);
    }

    [Test]
    public async Task PublicFind_UnseededTableReturnsMissingWithoutScanning()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();

        var missing = database.Find<MemoryPrimitiveRow>(42);

        await Assert.That(missing).IsNull();
        await Assert.That(database.Diagnostics.PrimaryKeyRequests).IsEqualTo(1);
        await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(1);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_GuidBackedTypedKeyNormalizesEachProbeAndReturnsModelValuedRowData()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();

            var cold = database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId));
            var warm = database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId));
            var missing = database.Find<MemoryConvertedRow>(new MemoryGuidId(Guid.Empty));

            await Assert.That(cold).IsNotNull();
            await Assert.That(cold!.Id).IsEqualTo(new MemoryGuidId(KnownId));
            await Assert.That(cold.DirectGuid).IsEqualTo(KnownDirectGuid);
            await Assert.That(cold.RelatedId).IsEqualTo(new MemoryGuidId(KnownRelatedId));
            await Assert.That(warm).IsSameReferenceAs(cold);
            await Assert.That(missing).IsNull();

            var table = database.Metadata.GetTableModel(typeof(MemoryConvertedRow)).Table;
            var modelRowData = cold.GetRowData();
            await Assert.That(modelRowData[table.GetColumnByDbName("id")])
                .IsTypeOf<MemoryGuidId>();
            await Assert.That(modelRowData[table.GetColumnByDbName("id")])
                .IsEqualTo(new MemoryGuidId(KnownId));
            await Assert.That(modelRowData[table.GetColumnByDbName("related_id")])
                .IsTypeOf<MemoryGuidId>();
            await Assert.That(modelRowData[table.GetColumnByDbName("related_id")])
                .IsEqualTo(new MemoryGuidId(KnownRelatedId));

            await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
                .IsEquivalentTo(
                    ["id", "related_id", "id", "id", "id", "id"],
                    CollectionOrdering.Matching);
            await Assert.That(MemoryGuidIdConverter.FromProviderColumns)
                .IsEquivalentTo(["id", "related_id"], CollectionOrdering.Matching);
            await Assert.That(database.Diagnostics.PrimaryKeyRequests).IsEqualTo(3);
            await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(3);
            await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    public async Task PublicFind_WrongModelKeyTypeIsRedactedAndRejectedBeforeStoreWork()
    {
        const string sensitiveKey = "lookup-secret-4827";
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 42, GroupId = 7, Name = "forty-two" }
        ]);
        var before = database.Diagnostics;

        var exception = Capture<MemoryLookupException>(() =>
            database.Find<MemoryPrimitiveRow>(sensitiveKey));

        await Assert.That(exception.Message).Contains("memory_primitive_rows.id");
        await Assert.That(exception.Message).Contains("System.Int32");
        await Assert.That(exception.ToString()).DoesNotContain(sensitiveKey);
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    public async Task PublicFind_CanonicalAndNumericSurrogatesRejectBeforeStoreWork()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 42, GroupId = 7, Name = "forty-two" }
        ]);
        var before = database.Diagnostics;
        object[] invalidKeys =
        [
            42L,
            DBNull.Value,
            DataLinqKey.FromValue(42)
        ];

        foreach (var invalidKey in invalidKeys)
        {
            var exception = Capture<MemoryLookupException>(() =>
                database.Find<MemoryPrimitiveRow>(invalidKey));

            await Assert.That(exception.Message).Contains("memory_primitive_rows.id");
            await Assert.That(exception.InnerException).IsNull();
            await Assert.That(database.Diagnostics).IsEqualTo(before);
        }
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_RawCanonicalGuidDoesNotSubstituteForTypedModelKey()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();
            var beforeDiagnostics = database.Diagnostics;
            var beforeConversions = MemoryGuidIdConverter.ToProviderColumns.ToArray();

            var exception = Capture<MemoryLookupException>(() =>
                database.Find<MemoryConvertedRow>(KnownId));

            await Assert.That(exception.Message).Contains("memory_converted_rows.id");
            await Assert.That(exception.Message).Contains(typeof(MemoryGuidId).FullName!);
            await Assert.That(exception.ToString()).DoesNotContain(KnownId.ToString());
            await Assert.That(exception.InnerException).IsNull();
            await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
                .IsEquivalentTo(beforeConversions, CollectionOrdering.Matching);
            await Assert.That(database.Diagnostics).IsEqualTo(beforeDiagnostics);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    public async Task PublicFind_NullKeyRejectsBeforeStoreWork()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var before = database.Diagnostics;

        var exception = Capture<ArgumentNullException>(() =>
            database.Find<MemoryPrimitiveRow>(null!));

        await Assert.That(exception.ParamName).IsEqualTo("modelPrimaryKey");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    public async Task PublicFind_CompositePrimaryKeyRejectsBeforeStoreWork()
    {
        var database = new MemoryDatabase<MemoryCompositeDatabase>();
        var before = database.Diagnostics;

        var exception = Capture<MemoryLookupException>(() =>
            database.Find<MemoryCompositeRow>("composite-secret-5931"));

        await Assert.That(exception.Message).Contains("memory_composite_rows");
        await Assert.That(exception.Message).Contains("exactly one primary-key column");
        await Assert.That(exception.Message).Contains("declares 2");
        await Assert.That(exception.ToString()).DoesNotContain("composite-secret-5931");
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    public async Task PublicFind_SeparateStoresReturnTheirOwnRowsAndIdentities()
    {
        var left = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var right = new MemoryDatabase<MemoryPrimitiveDatabase>();
        left.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 7, GroupId = 1, Name = "left" }
        ]);
        right.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 7, GroupId = 2, Name = "right" }
        ]);

        var leftRow = left.Find<MemoryPrimitiveRow>(7);
        var rightRow = right.Find<MemoryPrimitiveRow>(7);

        await Assert.That(leftRow).IsNotNull();
        await Assert.That(rightRow).IsNotNull();
        await Assert.That(leftRow!.Name).IsEqualTo("left");
        await Assert.That(rightRow!.Name).IsEqualTo("right");
        await Assert.That(leftRow).IsNotSameReferenceAs(rightRow);
        await Assert.That(leftRow.GetReadSource()).IsNotSameReferenceAs(rightRow.GetReadSource());
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_ToProviderConverterFailureIsRedactedBeforeStoreWork()
    {
        const string sensitiveFailure = "lookup-converter-secret-7142";
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();
            var before = database.Diagnostics;
            MemoryGuidIdConverter.SetToProviderProbe(_ =>
                throw new InvalidOperationException(sensitiveFailure));

            var exception = Capture<MemoryLookupException>(() =>
                database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId)));

            await Assert.That(exception.Message).Contains("memory_converted_rows.id");
            await Assert.That(exception.ToString()).DoesNotContain(sensitiveFailure);
            await Assert.That(exception.InnerException).IsNull();
            await Assert.That(database.Diagnostics).IsEqualTo(before);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_FromProviderConverterFailureIsRedactedAndDoesNotPoisonTheCache()
    {
        const string sensitiveFailure = "materialization-converter-secret-8253";
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();
            MemoryGuidIdConverter.SetFromProviderProbe(columnName =>
            {
                if (columnName == "related_id")
                    throw new InvalidOperationException(sensitiveFailure);
            });

            var exception = Capture<MemoryLookupException>(() =>
                database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId)));

            await Assert.That(exception.Message).Contains("memory_converted_rows.id");
            await Assert.That(exception.Message).Contains("memory_converted_rows.related_id");
            await Assert.That(exception.Message).Contains(typeof(MemoryGuidId).FullName!);
            await Assert.That(exception.ToString()).DoesNotContain(sensitiveFailure);
            await Assert.That(exception.ToString()).DoesNotContain(KnownId.ToString());
            await Assert.That(exception.ToString()).DoesNotContain(KnownRelatedId.ToString());
            await Assert.That(exception.InnerException).IsNull();
            await Assert.That(database.Diagnostics.PrimaryKeyRequests).IsEqualTo(1);
            await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(1);
            await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(1);
            await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(1);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
            await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(0);
            await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);

            MemoryGuidIdConverter.SetFromProviderProbe(null);
            var recovered = database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId));

            await Assert.That(recovered).IsNotNull();
            await Assert.That(recovered!.Id).IsEqualTo(new MemoryGuidId(KnownId));
            await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(2);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
            await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(1);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_KeyCaptureConverterFailureIsRedactedAndDoesNotPoisonTheCache()
    {
        const string sensitiveFailure = "key-capture-converter-secret-9162";
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();
            var idProbeCount = 0;
            MemoryGuidIdConverter.SetToProviderProbe(columnName =>
            {
                if (columnName == "id" && ++idProbeCount == 2)
                    throw new InvalidOperationException(sensitiveFailure);
            });

            var exception = Capture<MemoryLookupException>(() =>
                database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId)));

            await Assert.That(exception.Message).Contains("memory_converted_rows.id");
            await Assert.That(exception.Message).Contains(typeof(MemoryGuidId).FullName!);
            await Assert.That(exception.ToString()).DoesNotContain(sensitiveFailure);
            await Assert.That(exception.ToString()).DoesNotContain(KnownId.ToString());
            await Assert.That(exception.InnerException).IsNull();
            await Assert.That(idProbeCount).IsEqualTo(2);
            await Assert.That(database.Diagnostics.PrimaryKeyRequests).IsEqualTo(1);
            await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(1);
            await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(1);
            await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(1);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
            await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(0);
            await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);

            MemoryGuidIdConverter.SetToProviderProbe(null);
            var recovered = database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId));

            await Assert.That(recovered).IsNotNull();
            await Assert.That(recovered!.Id).IsEqualTo(new MemoryGuidId(KnownId));
            await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(2);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
            await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(1);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_ToProviderCancellationAndFatalFailuresPreserveExceptionIdentity()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();
            var before = database.Diagnostics;
            Exception[] expectedExceptions =
            [
                new OperationCanceledException("lookup cancelled"),
                new OutOfMemoryException("lookup out of memory"),
                new AccessViolationException("lookup access violation")
            ];

            foreach (var expected in expectedExceptions)
            {
                MemoryGuidIdConverter.SetToProviderProbe(_ => throw expected);
                var actual = Capture<Exception>(() =>
                    database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId)));

                await Assert.That(actual).IsSameReferenceAs(expected);
                await Assert.That(database.Diagnostics).IsEqualTo(before);
            }
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_KeyCaptureCancellationAndFatalFailuresPreserveExceptionIdentity()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();
            Exception[] expectedExceptions =
            [
                new OperationCanceledException("key capture cancelled"),
                new OutOfMemoryException("key capture out of memory"),
                new AccessViolationException("key capture access violation")
            ];

            foreach (var expected in expectedExceptions)
            {
                var idProbeCount = 0;
                MemoryGuidIdConverter.SetToProviderProbe(columnName =>
                {
                    if (columnName == "id" && ++idProbeCount == 2)
                        throw expected;
                });
                var actual = Capture<Exception>(() =>
                    database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId)));

                await Assert.That(actual).IsSameReferenceAs(expected);
                await Assert.That(idProbeCount).IsEqualTo(2);
            }

            await Assert.That(database.Diagnostics.PrimaryKeyRequests).IsEqualTo(3);
            await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(3);
            await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(3);
            await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(3);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
            await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(0);
            await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    [NotInParallel]
    public async Task PublicFind_FromProviderCancellationAndFatalFailuresPreserveExceptionIdentity()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var database = CreateConvertedDatabase();
            Exception[] expectedExceptions =
            [
                new OperationCanceledException("lookup materialization cancelled"),
                new OutOfMemoryException("lookup materialization out of memory"),
                new AccessViolationException("lookup materialization access violation")
            ];

            foreach (var expected in expectedExceptions)
            {
                MemoryGuidIdConverter.SetFromProviderProbe(_ => throw expected);
                var actual = Capture<Exception>(() =>
                    database.Find<MemoryConvertedRow>(new MemoryGuidId(KnownId)));

                await Assert.That(actual).IsSameReferenceAs(expected);
            }

            await Assert.That(database.Diagnostics.PrimaryKeyRequests).IsEqualTo(3);
            await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(3);
            await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(3);
            await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(3);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
            await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(0);
            await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    private static MemoryDatabase<MemoryConvertedDatabase> CreateConvertedDatabase()
    {
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        return database.Seed<MemoryConvertedRow>(
        [
            new MutableMemoryConvertedRow
            {
                Id = new MemoryGuidId(KnownId),
                DirectGuid = KnownDirectGuid,
                RelatedId = new MemoryGuidId(KnownRelatedId),
                OptionalRelatedId = null
            }
        ]);
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

[UseCache]
[Database("memory_composite")]
public sealed partial class MemoryCompositeDatabase(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<MemoryCompositeRow> Rows { get; } = new(readSource);
}

[Table("memory_composite_rows")]
public abstract partial class MemoryCompositeRow :
    Immutable<MemoryCompositeRow, MemoryCompositeDatabase>,
    ITableModel<MemoryCompositeDatabase>
{
    protected MemoryCompositeRow(
        IRowData rowData,
        IDataSourceAccess dataSource)
        : base(rowData, dataSource)
    {
    }

    protected MemoryCompositeRow(
        IRowData rowData,
        IDataLinqReadSource readSource)
        : base(rowData, readSource)
    {
    }

    [PrimaryKey]
    [Column("partition_id")]
    public abstract int PartitionId { get; }

    [PrimaryKey]
    [Column("row_id")]
    public abstract int RowId { get; }
}
