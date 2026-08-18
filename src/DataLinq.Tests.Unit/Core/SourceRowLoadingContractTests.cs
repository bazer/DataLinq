using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class SourceRowLoadingContractTests
{
    [Test]
    public async Task PrimaryKeyRequest_OwnsAndValidatesCanonicalProviderKeys()
    {
        var table = CreateMetadata().TableModels[0].Table;
        var callerOwnedKeys = new[] { DataLinqKey.FromValue(42) };
        using var cancellation = new CancellationTokenSource();

        var request = new SourcePrimaryKeyRowRequest(
            table,
            callerOwnedKeys,
            cancellation.Token);
        callerOwnedKeys[0] = DataLinqKey.FromValue(99);

        await Assert.That(request.Table).IsSameReferenceAs(table);
        await Assert.That(request.CanonicalProviderKeys.Length).IsEqualTo(1);
        await Assert.That(request.CanonicalProviderKeys[0].GetValue(0)).IsEqualTo(42);
        await Assert.That(request.CancellationToken).IsEqualTo(cancellation.Token);

        var modelKeyFailure = Capture<ArgumentException>(() =>
            new SourcePrimaryKeyRowRequest(
                table,
                [DataLinqKey.FromValue(new ModelId(42))]));
        await Assert.That(modelKeyFailure.Message).Contains("requires CLR type 'System.Int32'");
        await Assert.That(modelKeyFailure.Message).Contains(typeof(ModelId).FullName!);

        var nullKeyFailure = Capture<ArgumentException>(() =>
            new SourcePrimaryKeyRowRequest(table, [DataLinqKey.Null]));
        await Assert.That(nullKeyFailure.Message).Contains("contains a null component");

        var emptyFailure = Capture<ArgumentException>(() =>
            new SourcePrimaryKeyRowRequest(table, []));
        await Assert.That(emptyFailure.Message).Contains("at least one canonical provider key");
    }

    [Test]
    public async Task IndexRowRequest_OwnsAndValidatesFrozenIndexAndCanonicalProviderKey()
    {
        var metadata = CreateMetadata();
        var table = metadata.TableModels[0].Table;
        var otherTable = metadata.TableModels[1].Table;
        var nameIndex = table.ColumnIndices.Single(x => x.Name == "ix_source_rows_name");
        var payloadIndex = table.ColumnIndices.Single(x => x.Name == "ix_source_rows_payload");
        using var cancellation = new CancellationTokenSource();
        var callerOwnedBytes = new byte[] { 1, 2, 3 };

        var request = new SourceIndexRowRequest(
            table,
            payloadIndex,
            DataLinqKey.FromValue(callerOwnedBytes),
            cancellation.Token);
        callerOwnedBytes[0] = 99;
        var exposedBytes = (byte[])request.CanonicalProviderIndexKey.GetValue(0)!;
        exposedBytes[1] = 99;

        await Assert.That(request.Table).IsSameReferenceAs(table);
        await Assert.That(request.Index).IsSameReferenceAs(payloadIndex);
        await Assert.That((byte[])request.CanonicalProviderIndexKey.GetValue(0)!).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(request.CancellationToken).IsEqualTo(cancellation.Token);

        var modelKeyFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowRequest(
                table,
                nameIndex,
                DataLinqKey.FromValue(new ModelId(42))));
        await Assert.That(modelKeyFailure.Message).Contains("requires CLR type 'System.String'");
        await Assert.That(modelKeyFailure.Message).Contains(typeof(ModelId).FullName!);

        var nullKeyFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowRequest(table, nameIndex, DataLinqKey.Null));
        await Assert.That(nullKeyFailure.Message).Contains("contains a null component");

        var shapeFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowRequest(
                table,
                nameIndex,
                DataLinqKey.FromValues(["Ada", "extra"])));
        await Assert.That(shapeFailure.Message).Contains("has 2 components, expected 1");

        var foreignIndex = otherTable.ColumnIndices.Single(x => x.Name == "ix_other_source_rows_name");
        var foreignIndexFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowRequest(
                table,
                foreignIndex,
                DataLinqKey.FromValue("Ada")));
        await Assert.That(foreignIndexFailure.Message).Contains("does not belong to table");

        var detachedIndex = new ColumnIndex(
            "ix_detached",
            IndexCharacteristic.Simple,
            IndexType.BTREE,
            [table.GetColumnByDbName("name")]);
        var mutableIndexFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowRequest(
                table,
                detachedIndex,
                DataLinqKey.FromValue("Ada")));
        await Assert.That(mutableIndexFailure.Message).Contains("index 'ix_detached' is still mutable");

        var mutableTableFailure = Capture<InvalidOperationException>(() =>
            new SourceIndexRowRequest(
                new TableDefinition("mutable_rows"),
                nameIndex,
                DataLinqKey.FromValue("Ada")));
        await Assert.That(mutableTableFailure.Message).Contains("table 'mutable_rows' is still mutable");

        var tableWithoutPrimaryKey = CreateMetadata(includePrimaryKey: false).TableModels[0].Table;
        var noPrimaryKeyFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowRequest(
                tableWithoutPrimaryKey,
                tableWithoutPrimaryKey.ColumnIndices.Single(x => x.Name == "ix_source_rows_name"),
                DataLinqKey.FromValue("Ada")));
        await Assert.That(noPrimaryKeyFailure.Message).Contains("has no primary key");
    }

    [Test]
    public async Task RowLoadResult_OwnsFiniteRowsAndRejectsCrossTablePayloads()
    {
        var metadata = CreateMetadata();
        var table = metadata.TableModels[0].Table;
        var otherTable = metadata.TableModels[1].Table;
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(42)]);
        var row = CreateCanonicalRow(table, 42, "Ada");
        var callerOwnedRows = new List<CanonicalProviderValueRow> { row };

        var result = new SourceRowLoadResult(request, callerOwnedRows);
        callerOwnedRows.Clear();

        await Assert.That(result.Request).IsSameReferenceAs(request);
        await Assert.That(result.Table).IsSameReferenceAs(table);
        await Assert.That(result.Rows.Length).IsEqualTo(1);
        await Assert.That(result.Rows[0].ProviderRow).IsSameReferenceAs(row);
        await Assert.That(result.Rows[0].CanonicalProviderKey).IsEqualTo(DataLinqKey.FromValue(42));
        await Assert.That(typeof(IDisposable).IsAssignableFrom(typeof(SourceRowLoadResult))).IsFalse();

        var crossTableFailure = Capture<ArgumentException>(() =>
            new SourceRowLoadResult(
                request,
                [CreateCanonicalRow(otherTable, 42, "Wrong table")]));
        await Assert.That(crossTableFailure.Message).Contains("contains a row from table");
        await Assert.That(crossTableFailure.Message).Contains(otherTable.DbName);

        var unrequestedKeyFailure = Capture<ArgumentException>(() =>
            new SourceRowLoadResult(
                request,
                [CreateCanonicalRow(table, 43, "Unrequested")]));
        await Assert.That(unrequestedKeyFailure.Message).Contains("unrequested primary key");

        var duplicateKeyFailure = Capture<ArgumentException>(() =>
            new SourceRowLoadResult(
                request,
                [
                    CreateCanonicalRow(table, 42, "Ada"),
                    CreateCanonicalRow(table, 42, "Duplicate")
                ]));
        await Assert.That(duplicateKeyFailure.Message).Contains("duplicate primary key");
    }

    [Test]
    public async Task SingularResultValidation_RejectsCrossTableAndUnrequestedRows()
    {
        var metadata = CreateMetadata();
        var table = metadata.TableModels[0].Table;
        var otherTable = metadata.TableModels[1].Table;
        var requestedKey = DataLinqKey.FromValue(42);
        var validRow = CreateCanonicalRow(table, 42, "Ada");

        SourceRowLoadingValidation.ValidateSingleResult(
            table,
            in requestedKey,
            validRow,
            "Test loader");

        var crossTableFailure = Capture<InvalidOperationException>(() =>
            SourceRowLoadingValidation.ValidateSingleResult(
                table,
                in requestedKey,
                CreateCanonicalRow(otherTable, 42, "Wrong table"),
                "Test loader"));
        await Assert.That(crossTableFailure.Message).Contains("returned a row from table");

        var unrequestedFailure = Capture<InvalidOperationException>(() =>
            SourceRowLoadingValidation.ValidateSingleResult(
                table,
                in requestedKey,
                CreateCanonicalRow(table, 43, "Unrequested"),
                "Test loader"));
        await Assert.That(unrequestedFailure.Message).Contains("unrequested primary key");
    }

    [Test]
    public async Task IndexRowLoadResult_OwnsFiniteRowsAndLeavesMatchingEqualityToBackend()
    {
        var metadata = CreateMetadata();
        var table = metadata.TableModels[0].Table;
        var otherTable = metadata.TableModels[1].Table;
        var index = table.ColumnIndices.Single(x => x.Name == "ix_source_rows_name");
        var request = new SourceIndexRowRequest(
            table,
            index,
            DataLinqKey.FromValue("Ada"));
        var backendMatchedRow = CreateCanonicalRow(table, 42, "Different by CLR equality");
        var callerOwnedRows = new List<CanonicalProviderValueRow> { backendMatchedRow };

        var result = new SourceIndexRowLoadResult(request, callerOwnedRows);
        callerOwnedRows.Clear();

        await Assert.That(result.Request).IsSameReferenceAs(request);
        await Assert.That(result.Table).IsSameReferenceAs(table);
        await Assert.That(result.Index).IsSameReferenceAs(index);
        await Assert.That(result.Rows.Length).IsEqualTo(1);
        await Assert.That(result.Rows[0].ProviderRow).IsSameReferenceAs(backendMatchedRow);
        await Assert.That(result.Rows[0].CanonicalProviderKey).IsEqualTo(DataLinqKey.FromValue(42));
        await Assert.That(typeof(IDisposable).IsAssignableFrom(typeof(SourceIndexRowLoadResult))).IsFalse();

        var nullRowFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowLoadResult(request, [null!]));
        await Assert.That(nullRowFailure.Message).Contains("contains a null row");

        var crossTableFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowLoadResult(
                request,
                [CreateCanonicalRow(otherTable, 42, "Wrong table")]));
        await Assert.That(crossTableFailure.Message).Contains("contains a row from table");
        await Assert.That(crossTableFailure.Message).Contains(otherTable.DbName);

        var duplicateKeyFailure = Capture<ArgumentException>(() =>
            new SourceIndexRowLoadResult(
                request,
                [
                    CreateCanonicalRow(table, 42, "Ada"),
                    CreateCanonicalRow(table, 42, "Backend-equivalent Ada")
                ]));
        await Assert.That(duplicateKeyFailure.Message).Contains("duplicate primary key");
    }

    [Test]
    public async Task IndexRowLoadResult_LargeValidationRejectsDuplicatePrimaryKeys()
    {
        var metadata = CreateMetadata();
        var table = metadata.TableModels[0].Table;
        var index = table.ColumnIndices.Single(x => x.Name == "ix_source_rows_name");
        var request = new SourceIndexRowRequest(
            table,
            index,
            DataLinqKey.FromValue("Ada"));
        var rows = Enumerable.Range(0, SourceRowLoadResult.LinearValidationThreshold + 1)
            .Select(id => CreateCanonicalRow(table, id, $"row-{id}"))
            .Append(CreateCanonicalRow(table, 0, "duplicate"));

        var failure = Capture<ArgumentException>(() =>
            new SourceIndexRowLoadResult(request, rows));

        await Assert.That(failure.Message).Contains("duplicate primary key");
    }

    [Test]
    public async Task RowLoadResult_CanonicalBinaryKeyDoesNotAliasCallerOrRowBuffers()
    {
        var table = CreateBinaryKeyTable();
        var idColumn = table.GetColumnByDbName("id");
        var callerBytes = new byte[] { 1, 2, 3 };
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(callerBytes)]);
        var providerRow = CanonicalProviderValueRow.Create(
            table,
            new object?[] { callerBytes, "Ada" });

        var result = new SourceRowLoadResult(request, [providerRow]);
        callerBytes[0] = 91;
        var exposedRowBytes = (byte[])providerRow[idColumn]!;
        exposedRowBytes[0] = 92;
        var exposedKeyBytes = (byte[])result.Rows[0].CanonicalProviderKey.GetValue(0)!;
        exposedKeyBytes[0] = 93;

        await Assert.That((byte[])providerRow[idColumn]!).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That((byte[])result.Rows[0].CanonicalProviderKey.GetValue(0)!)
            .IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(result.Rows[0].CanonicalProviderKey)
            .IsEqualTo(DataLinqKey.FromValue(new byte[] { 1, 2, 3 }));
    }

    [Test]
    [Arguments(SourceRowLoadResult.LinearValidationThreshold - 1)]
    [Arguments(SourceRowLoadResult.LinearValidationThreshold)]
    [Arguments(SourceRowLoadResult.LinearValidationThreshold + 1)]
    public async Task RowLoadResult_ValidatesBelowAtAndAboveLinearThreshold(int rowCount)
    {
        var table = CreateMetadata().TableModels[0].Table;
        var keys = Enumerable.Range(0, rowCount)
            .Select(DataLinqKey.FromValue)
            .ToArray();
        var rows = Enumerable.Range(0, rowCount)
            .Select(id => CreateCanonicalRow(table, id, $"row-{id}"))
            .ToArray();
        var request = new SourcePrimaryKeyRowRequest(table, keys);

        var result = new SourceRowLoadResult(request, rows);

        await Assert.That(result.Rows.Length).IsEqualTo(rowCount);
        await Assert.That(result.Rows.Select(static row => (int)row.CanonicalProviderKey.GetValue(0)!).ToArray())
            .IsEquivalentTo(Enumerable.Range(0, rowCount).ToArray());
    }

    [Test]
    public async Task RowLoadResult_LargeValidationPreservesMissingExtraAndDuplicateChecks()
    {
        var table = CreateMetadata().TableModels[0].Table;
        var request = new SourcePrimaryKeyRowRequest(
            table,
            Enumerable.Range(0, SourceRowLoadResult.LinearValidationThreshold + 2)
                .Select(DataLinqKey.FromValue));
        var missingResult = new SourceRowLoadResult(
            request,
            Enumerable.Range(0, SourceRowLoadResult.LinearValidationThreshold)
                .Select(id => CreateCanonicalRow(table, id, $"row-{id}")));

        await Assert.That(missingResult.Rows.Length)
            .IsEqualTo(SourceRowLoadResult.LinearValidationThreshold);

        var extraFailure = Capture<ArgumentException>(() =>
            new SourceRowLoadResult(
                request,
                [CreateCanonicalRow(table, 999, "extra")]));
        await Assert.That(extraFailure.Message).Contains("unrequested primary key");

        var duplicateRows = Enumerable.Range(0, SourceRowLoadResult.LinearValidationThreshold + 1)
            .Select(id => CreateCanonicalRow(table, id, $"row-{id}"))
            .Append(CreateCanonicalRow(table, 0, "duplicate"));
        var duplicateFailure = Capture<ArgumentException>(() =>
            new SourceRowLoadResult(request, duplicateRows));
        await Assert.That(duplicateFailure.Message).Contains("duplicate primary key");
    }

    [Test]
    public async Task BorrowedPrimaryKeyRequest_UsesBoundedReadOnlySlice()
    {
        var table = CreateMetadata().TableModels[0].Table;
        var stableKeys = Enumerable.Range(0, SourceRowLoadResult.LinearValidationThreshold + 4)
            .Select(DataLinqKey.FromValue)
            .ToList();

        var request = SourcePrimaryKeyRowRequest.Borrow(
            table,
            stableKeys,
            offset: 2,
            count: SourceRowLoadResult.LinearValidationThreshold);

        await Assert.That(request.CanonicalProviderKeys.Length)
            .IsEqualTo(SourceRowLoadResult.LinearValidationThreshold);
        await Assert.That(request.CanonicalProviderKeys[0]).IsEqualTo(DataLinqKey.FromValue(2));
        await Assert.That(request.CanonicalProviderKeys[^1]).IsEqualTo(
            DataLinqKey.FromValue(SourceRowLoadResult.LinearValidationThreshold + 1));

        var emptyFailure = Capture<ArgumentException>(() =>
            SourcePrimaryKeyRowRequest.Borrow(table, stableKeys, 0, 0));
        await Assert.That(emptyFailure.Message).Contains("requires at least one");

        _ = Capture<ArgumentException>(() =>
            SourcePrimaryKeyRowRequest.Borrow(table, stableKeys, stableKeys.Count - 1, 3));
    }

    [Test]
    public async Task BorrowedPrimaryKeyRequest_CoversExactAndMultiChunkBoundaries()
    {
        const int chunkSize = 500;
        var table = CreateMetadata().TableModels[0].Table;
        var stableKeys = Enumerable.Range(0, chunkSize * 2 + 1)
            .Select(DataLinqKey.FromValue)
            .ToList();
        var chunks = new List<SourcePrimaryKeyRowRequest>();

        for (var offset = 0; offset < stableKeys.Count; offset += chunkSize)
        {
            chunks.Add(SourcePrimaryKeyRowRequest.Borrow(
                table,
                stableKeys,
                offset,
                Math.Min(chunkSize, stableKeys.Count - offset)));
        }

        await Assert.That(chunks.Count).IsEqualTo(3);
        await Assert.That(chunks[0].CanonicalProviderKeys.Length).IsEqualTo(chunkSize);
        await Assert.That(chunks[0].CanonicalProviderKeys[^1]).IsEqualTo(DataLinqKey.FromValue(499));
        await Assert.That(chunks[1].CanonicalProviderKeys[0]).IsEqualTo(DataLinqKey.FromValue(500));
        await Assert.That(chunks[1].CanonicalProviderKeys[^1]).IsEqualTo(DataLinqKey.FromValue(999));
        await Assert.That(chunks[2].CanonicalProviderKeys.Length).IsEqualTo(1);
        await Assert.That(chunks[2].CanonicalProviderKeys[0]).IsEqualTo(DataLinqKey.FromValue(1000));
    }

    [Test]
    public async Task ResultBuilder_TransfersOwnedStorageBehindReadOnlyViewAndRejectsReuse()
    {
        var table = CreateMetadata().TableModels[0].Table;
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(1), DataLinqKey.FromValue(2)]);
        var builder = new SourceRowLoadResult.Builder(request, capacity: 2);
        builder.Add(CreateCanonicalRow(table, 1, "one"));
        builder.Add(CreateCanonicalRow(table, 2, "two"));

        var result = builder.Build();

        await Assert.That(result.Rows.Length).IsEqualTo(2);
        await Assert.That((object)result.Rows is IList<LoadedCanonicalRow>).IsFalse();
        var reuseFailure = Capture<InvalidOperationException>(() =>
            builder.Add(CreateCanonicalRow(table, 1, "reuse")));
        await Assert.That(reuseFailure.Message).Contains("cannot be reused");
    }

    [Test]
    public async Task RowLoaderContract_SeparatesSingularAndBatchLoadingAndCarriesCancellation()
    {
        var methods = typeof(ISourceRowLoader).GetMethods();
        var batchMethod = methods.Single(method => method.Name == nameof(ISourceRowLoader.Load));
        var singularMethod = methods.Single(method => method.Name == nameof(ISourceRowLoader.LoadSingle));
        var batchParameter = batchMethod.GetParameters().Single();
        var singularParameters = singularMethod.GetParameters();

        await Assert.That(batchMethod.ReturnType).IsEqualTo(typeof(SourceRowLoadResult));
        await Assert.That(batchParameter.ParameterType).IsEqualTo(typeof(SourcePrimaryKeyRowRequest));
        await Assert.That(singularMethod.ReturnType).IsEqualTo(typeof(CanonicalProviderValueRow));
        await Assert.That(singularParameters.Length).IsEqualTo(3);
        await Assert.That(singularParameters[0].ParameterType).IsEqualTo(typeof(TableDefinition));
        await Assert.That(singularParameters[1].ParameterType).IsEqualTo(typeof(DataLinqKey).MakeByRefType());
        await Assert.That(singularParameters[1].IsIn).IsTrue();
        await Assert.That(singularParameters[2].ParameterType).IsEqualTo(typeof(CancellationToken));
        await Assert.That(typeof(IDataLinqSourceRowServices).GetProperty(nameof(IDataLinqSourceRowServices.RowLoader))!.PropertyType)
            .IsEqualTo(typeof(ISourceRowLoader));

        var table = CreateMetadata().TableModels[0].Table;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(42)],
            cancellation.Token);
        var loader = new RecordingLoader();
        var key = DataLinqKey.FromValue(42);

        var singularException = Capture<OperationCanceledException>(() =>
            loader.LoadSingle(table, in key, cancellation.Token));
        var batchException = Capture<OperationCanceledException>(() => loader.Load(request));

        await Assert.That(singularException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(batchException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(loader.BackendWorkStarted).IsFalse();
    }

    [Test]
    public async Task IndexRowLoaderContract_IsOptionalAndCarriesCancellationBeforeBackendWork()
    {
        var method = typeof(ISourceIndexRowLoader).GetMethods().Single();
        var parameter = method.GetParameters().Single();

        await Assert.That(method.Name).IsEqualTo(nameof(ISourceIndexRowLoader.Load));
        await Assert.That(method.ReturnType).IsEqualTo(typeof(SourceIndexRowLoadResult));
        await Assert.That(parameter.ParameterType).IsEqualTo(typeof(SourceIndexRowRequest));
        await Assert.That(typeof(IDataLinqIndexRowServices).GetProperty(nameof(IDataLinqIndexRowServices.IndexRowLoader))!.PropertyType)
            .IsEqualTo(typeof(ISourceIndexRowLoader));
        await Assert.That(typeof(IDataLinqSourceRowServices).IsAssignableFrom(typeof(IDataLinqIndexRowServices))).IsFalse();
        await Assert.That(typeof(IDataLinqIndexRowServices).IsAssignableFrom(typeof(IDataLinqSourceRowServices))).IsFalse();

        var table = CreateMetadata().TableModels[0].Table;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new SourceIndexRowRequest(
            table,
            table.ColumnIndices.Single(x => x.Name == "ix_source_rows_name"),
            DataLinqKey.FromValue("Ada"),
            cancellation.Token);
        var loader = new RecordingIndexLoader();

        var exception = Capture<OperationCanceledException>(() => loader.Load(request));

        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(loader.BackendWorkStarted).IsFalse();
    }

    private static CanonicalProviderValueRow CreateCanonicalRow(
        TableDefinition table,
        int id,
        string name)
    {
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = id;
        values[table.GetColumnByDbName("name").Index] = name;
        values[table.GetColumnByDbName("payload").Index] = new byte[] { 1, 2, 3 };
        return CanonicalProviderValueRow.Create(table, values);
    }

    private static DatabaseDefinition CreateMetadata(bool includePrimaryKey = true)
    {
        var draft = new MetadataDatabaseDraft(
            "SourceRowLoadingContractDb",
            new CsTypeDeclaration(typeof(SourceRowLoadingContractTests)))
        {
            TableModels =
            [
                CreateTableModel("Rows", "source_rows", typeof(SourceRowModel), includePrimaryKey),
                CreateTableModel("OtherRows", "other_source_rows", typeof(OtherSourceRowModel), includePrimaryKey)
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static TableDefinition CreateBinaryKeyTable()
    {
        var draft = new MetadataDatabaseDraft(
            "SourceRowLoadingBinaryKeyDb",
            new CsTypeDeclaration(typeof(SourceRowLoadingContractTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(BinaryKeySourceRowModel)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(byte[])),
                                new MetadataColumnDraft("id") { PrimaryKey = true })
                            {
                                CsSize = 3
                            },
                            new MetadataValuePropertyDraft(
                                "Name",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("name"))
                        ]
                    },
                    new MetadataTableDraft("binary_key_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;
    }

    private static MetadataTableModelDraft CreateTableModel(
        string propertyName,
        string tableName,
        Type modelType,
        bool includePrimaryKey) =>
        new(
            propertyName,
            new MetadataModelDraft(new CsTypeDeclaration(modelType))
            {
                ValueProperties =
                [
                    new MetadataValuePropertyDraft(
                        "Id",
                        new CsTypeDeclaration(typeof(int)),
                        new MetadataColumnDraft("id") { PrimaryKey = includePrimaryKey })
                    {
                        CsSize = sizeof(int)
                    },
                    new MetadataValuePropertyDraft(
                        "Name",
                        new CsTypeDeclaration(typeof(string)),
                        new MetadataColumnDraft("name"))
                    {
                        Attributes =
                        [
                            new IndexAttribute(
                                $"ix_{tableName.Replace('-', '_')}_name",
                                IndexCharacteristic.Simple,
                                IndexType.BTREE)
                        ]
                    },
                    new MetadataValuePropertyDraft(
                        "Payload",
                        new CsTypeDeclaration(typeof(byte[])),
                        new MetadataColumnDraft("payload"))
                    {
                        CsSize = 3,
                        Attributes =
                        [
                            new IndexAttribute(
                                $"ix_{tableName.Replace('-', '_')}_payload",
                                IndexCharacteristic.Simple,
                                IndexType.BTREE)
                        ]
                    }
                ]
            },
            new MetadataTableDraft(tableName)
            {
                Type = includePrimaryKey ? TableType.Table : TableType.View,
                Definition = includePrimaryKey ? null : $"SELECT * FROM {tableName}"
            });

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

    private sealed record ModelId(int Value);
    private sealed class SourceRowModel;
    private sealed class OtherSourceRowModel;
    private sealed class BinaryKeySourceRowModel;

    private sealed class RecordingLoader : ISourceRowLoader
    {
        public bool BackendWorkStarted { get; private set; }

        public CanonicalProviderValueRow? LoadSingle(
            TableDefinition table,
            in DataLinqKey canonicalProviderKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BackendWorkStarted = true;
            return null;
        }

        public SourceRowLoadResult Load(SourcePrimaryKeyRowRequest request)
        {
            request.ThrowIfCancellationRequested();
            BackendWorkStarted = true;
            return new SourceRowLoadResult(request, []);
        }
    }

    private sealed class RecordingIndexLoader : ISourceIndexRowLoader
    {
        public bool BackendWorkStarted { get; private set; }

        public SourceIndexRowLoadResult Load(SourceIndexRowRequest request)
        {
            request.ThrowIfCancellationRequested();
            BackendWorkStarted = true;
            return new SourceIndexRowLoadResult(request, []);
        }
    }
}
