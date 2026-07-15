using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Memory;
using DataLinq.Metadata;

namespace DataLinq.Tests.Memory;

public sealed class MemoryPublicApiTests
{
    private static readonly Guid KnownId = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid KnownDirectGuid = new("f1e2d3c4-b5a6-4789-90ab-cdef12345678");
    private static readonly Guid KnownRelatedId = new("10213243-5465-7687-98a9-bacbdcedfe0f");

    [Test]
    public async Task ConstructSeedAndQuery_UsesOnlyTheFrozenPublicSurface()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();

        var returned = database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow
            {
                Id = 42,
                GroupId = 7,
                Name = "forty-two"
            },
            new MutableMemoryPrimitiveRow
            {
                Id = 5,
                GroupId = 3,
                Name = "five"
            }
        ]);

        var firstQueryModel = database.Query();
        var rows = firstQueryModel.Rows
            .OrderBy(static row => row.Id)
            .ToArray();

        await Assert.That(returned).IsSameReferenceAs(database);
        await Assert.That(database.Query()).IsSameReferenceAs(firstQueryModel);
        await Assert.That(rows.Select(static row => row.Id).ToArray())
            .IsEquivalentTo([5, 42]);
        await Assert.That(rows.Select(static row => row.Name).ToArray())
            .IsEquivalentTo(["five", "forty-two"]);
    }

    [Test]
    [NotInParallel]
    public async Task GeneratedMutableSeed_SnapshotsModelValuesAndUsesSharedConverters()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        var source = new MutableMemoryConvertedRow
        {
            Id = new MemoryGuidId(KnownId),
            DirectGuid = KnownDirectGuid,
            RelatedId = new MemoryGuidId(KnownRelatedId),
            OptionalRelatedId = null
        };

        database.Seed<MemoryConvertedRow>([source]);
        source.DirectGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        source.RelatedId = new MemoryGuidId(
            Guid.Parse("11111111-2222-3333-4444-555555555555"));

        var stored = database.Query().Rows.ToArray().Single();

        await Assert.That(stored.Id).IsEqualTo(new MemoryGuidId(KnownId));
        await Assert.That(stored.DirectGuid).IsEqualTo(KnownDirectGuid);
        await Assert.That(stored.RelatedId).IsEqualTo(new MemoryGuidId(KnownRelatedId));
        await Assert.That(stored.OptionalRelatedId).IsNull();
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["id", "related_id", "id"]);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns)
            .IsEquivalentTo(["id", "related_id"]);
    }

    [Test]
    public async Task SeparateMemoryDatabases_OwnRowsAndMaterializedIdentityIndependently()
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

        var leftRow = left.Query().Rows.ToArray().Single();
        var rightRow = right.Query().Rows.ToArray().Single();

        await Assert.That(leftRow.Name).IsEqualTo("left");
        await Assert.That(rightRow.Name).IsEqualTo("right");
        await Assert.That(leftRow).IsNotSameReferenceAs(rightRow);
        await Assert.That(leftRow.GetReadSource()).IsNotSameReferenceAs(rightRow.GetReadSource());
    }

    [Test]
    public async Task InvalidGeneratedMutableSeed_IsAtomicAndCanBeCorrected()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var invalid = new MutableMemoryPrimitiveRow
        {
            Id = 1,
            GroupId = 2,
            Name = null!
        };

        var exception = Capture<MemorySeedException>(() =>
            database.Seed<MemoryPrimitiveRow>([invalid]));

        await Assert.That(exception.Message).Contains("Model-valued memory seed row 0");
        await Assert.That(exception.Message).Contains("memory_primitive_rows.name");
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);

        database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 1, GroupId = 2, Name = "corrected" }
        ]);

        await Assert.That(database.Query().Rows.ToArray().Single().Name).IsEqualTo("corrected");
    }

    [Test]
    public async Task Seed_NullRowsUsesThePublicParameterName()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();

        var exception = Capture<ArgumentNullException>(() =>
            database.Seed<MemoryPrimitiveRow>(null!));

        await Assert.That(exception.ParamName).IsEqualTo("rows");
    }

    [Test]
    public async Task EnumeratorFailure_IsRedactedAtomicAndRetryable()
    {
        const string sensitiveFailure = "fixture-secret-8472";
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();

        var exception = Capture<MemorySeedException>(() =>
            database.Seed<MemoryPrimitiveRow>(FailingRows()));

        await Assert.That(exception.Message).Contains("failed before row 1");
        await Assert.That(exception.Message).DoesNotContain(sensitiveFailure);
        await Assert.That(exception.ToString()).DoesNotContain(sensitiveFailure);
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);

        database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 9, GroupId = 4, Name = "recovered" }
        ]);

        await Assert.That(database.Query().Rows.ToArray().Single().Name).IsEqualTo("recovered");

        static IEnumerable<Mutable<MemoryPrimitiveRow>> FailingRows()
        {
            yield return new MutableMemoryPrimitiveRow
            {
                Id = 8,
                GroupId = 4,
                Name = "discarded"
            };
            throw new InvalidOperationException(sensitiveFailure);
        }
    }

    [Test]
    public async Task EnumeratorCleanupFailure_DoesNotMaskOrLeakThePrimaryFailure()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();

        var exception = Capture<MemorySeedException>(() =>
            database.Seed<MemoryPrimitiveRow>(new DoubleFaultSeedRows()));

        await Assert.That(exception.Message).Contains("failed before row 0");
        await Assert.That(exception.ToString()).DoesNotContain(DoubleFaultSeedRows.MoveSecret);
        await Assert.That(exception.ToString()).DoesNotContain(DoubleFaultSeedRows.DisposeSecret);
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    public async Task EnumeratorPrimaryFailureAndCleanupCancellation_CleanupCancellationWins()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();

        var exception = Capture<OperationCanceledException>(() =>
            database.Seed<MemoryPrimitiveRow>(new CleanupCancelledSeedRows()));

        await Assert.That(exception.Message).Contains("cleanup cancelled");
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);

        database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 11, GroupId = 5, Name = "recovered" }
        ]);

        await Assert.That(database.Query().Rows.ToArray().Single().Name).IsEqualTo("recovered");
    }

    [Test]
    [NotInParallel]
    public async Task ConverterFailure_RedactsTheCompletePublicExceptionGraph()
    {
        const string sensitiveFailure = "converter-secret-6248";
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        var source = new MutableMemoryConvertedRow
        {
            Id = new MemoryGuidId(KnownId),
            DirectGuid = KnownDirectGuid,
            RelatedId = new MemoryGuidId(KnownRelatedId),
            OptionalRelatedId = null
        };
        MemoryGuidIdConverter.SetToProviderProbe(_ =>
            throw new InvalidOperationException(sensitiveFailure));

        MemorySeedException exception;
        try
        {
            exception = Capture<MemorySeedException>(() =>
                database.Seed<MemoryConvertedRow>([source]));
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }

        await Assert.That(exception.Message).Contains("memory_converted_rows.id");
        await Assert.That(exception.ToString()).DoesNotContain(sensitiveFailure);
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(database.GetStoredRowCount<MemoryConvertedRow>()).IsEqualTo(0);
    }

    [Test]
    public async Task ExistingMutableAccessorFailure_RedactsTheCompletePublicExceptionGraph()
    {
        const string sensitiveFailure = "row-accessor-secret-4196";
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var table = database.Metadata.GetTableModel(typeof(MemoryPrimitiveRow)).Table;
        var immutable = new ImmutableMemoryPrimitiveRow(
            new ThrowingSeedRowData(table, sensitiveFailure),
            database.ReadSource);
        var source = new MutableMemoryPrimitiveRow(immutable);

        var exception = Capture<MemorySeedException>(() =>
            database.Seed<MemoryPrimitiveRow>([source]));

        await Assert.That(exception.Message).Contains("could not be read");
        await Assert.That(exception.ToString()).DoesNotContain(sensitiveFailure);
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    public async Task ReseedAndEmptySeed_RejectBeforeEnumeratingTheNewSource()
    {
        var populated = new MemoryDatabase<MemoryPrimitiveDatabase>();
        populated.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 3, GroupId = 1, Name = "three" }
        ]);
        var enumerated = false;

        var populatedException = Capture<MemorySeedException>(() =>
            populated.Seed<MemoryPrimitiveRow>(UnexpectedRows()));

        await Assert.That(populatedException.Message).Contains("has already been seeded");
        await Assert.That(populatedException.Message).DoesNotContain("spike");
        await Assert.That(enumerated).IsFalse();

        var empty = new MemoryDatabase<MemoryPrimitiveDatabase>();
        empty.Seed<MemoryPrimitiveRow>([]);
        var emptyException = Capture<MemorySeedException>(() =>
            empty.Seed<MemoryPrimitiveRow>(UnexpectedRows()));

        await Assert.That(emptyException.Message).Contains("has already been seeded");
        await Assert.That(enumerated).IsFalse();
        await Assert.That(empty.Query().Rows.ToArray()).IsEmpty();

        IEnumerable<Mutable<MemoryPrimitiveRow>> UnexpectedRows()
        {
            enumerated = true;
            yield return new MutableMemoryPrimitiveRow
            {
                Id = 99,
                GroupId = 99,
                Name = "must-not-be-read"
            };
        }
    }

    [Test]
    public async Task UnsupportedQuery_ExposesStructuredValueRedactedCapabilityDiagnostic()
    {
        const string sensitiveName = "captured-secret-5291";
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        database.Seed<MemoryPrimitiveRow>(
        [
            new MutableMemoryPrimitiveRow { Id = 1, GroupId = 2, Name = "one" }
        ]);

        var exception = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Select(row => sensitiveName + row.Name)
                .ToArray());

        await Assert.That(exception.BackendName).IsEqualTo("memory");
        await Assert.That(exception.Feature).IsEqualTo("Projection:ComputedRowLocalExpression");
        await Assert.That(exception.Location).IsEqualTo("projection");
        await Assert.That(exception.SourceId).IsNull();
        await Assert.That(exception.ColumnName).IsNull();
        await Assert.That(exception.Message).DoesNotContain(sensitiveName);
    }

    [Test]
    public async Task PublicSurface_ExposesOnlyConstructionSeedQueryAndCatchableDiagnostics()
    {
        var databaseType = typeof(MemoryDatabase<>);
        var declaredMethods = databaseType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(static method => method.Name)
            .Order()
            .ToArray();

        await Assert.That(databaseType.IsPublic).IsTrue();
        await Assert.That(databaseType.GetConstructors().Length).IsEqualTo(1);
        await Assert.That(declaredMethods).IsEquivalentTo(["Query", "Seed"]);
        await Assert.That(databaseType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .IsEmpty();
        await Assert.That(typeof(MemorySeedException).IsPublic).IsTrue();
        await Assert.That(typeof(QueryBackendCapabilityException).IsPublic).IsTrue();
        await Assert.That(typeof(MemorySeedException)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)).IsEmpty();
        await Assert.That(typeof(QueryBackendCapabilityException)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)).IsEmpty();
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

    private sealed class DoubleFaultSeedRows : IEnumerable<Mutable<MemoryPrimitiveRow>>
    {
        internal const string MoveSecret = "move-secret-1385";
        internal const string DisposeSecret = "dispose-secret-9713";

        public IEnumerator<Mutable<MemoryPrimitiveRow>> GetEnumerator() => new Enumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<Mutable<MemoryPrimitiveRow>>
        {
            public Mutable<MemoryPrimitiveRow> Current =>
                throw new InvalidOperationException("Current must not be read.");

            object IEnumerator.Current => Current;

            public bool MoveNext() => throw new InvalidOperationException(MoveSecret);

            public void Reset() => throw new NotSupportedException();

            public void Dispose() => throw new InvalidOperationException(DisposeSecret);
        }
    }

    private sealed class CleanupCancelledSeedRows : IEnumerable<Mutable<MemoryPrimitiveRow>>
    {
        public IEnumerator<Mutable<MemoryPrimitiveRow>> GetEnumerator() => new Enumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<Mutable<MemoryPrimitiveRow>>
        {
            public Mutable<MemoryPrimitiveRow> Current =>
                throw new InvalidOperationException("Current must not be read.");

            object IEnumerator.Current => Current;

            public bool MoveNext() => throw new InvalidOperationException("primary seed failure");

            public void Reset() => throw new NotSupportedException();

            public void Dispose() => throw new OperationCanceledException("cleanup cancelled");
        }
    }

    private sealed class ThrowingSeedRowData(
        TableDefinition table,
        string sensitiveFailure) : IRowData
    {
        public TableDefinition Table { get; } = table;

        public object? this[ColumnDefinition column] => GetValue(column);

        public object? this[int columnIndex] => GetValue(columnIndex);

        public object? GetValue(ColumnDefinition column) => column.DbName switch
        {
            "id" => 71,
            "group_id" => 9,
            _ => throw new InvalidOperationException(sensitiveFailure)
        };

        public object? GetValue(int columnIndex) => GetValue(Table.Columns[columnIndex]);

        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(GetValue);

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column =>
                new KeyValuePair<ColumnDefinition, object?>(column, GetValue(column)));
    }
}
