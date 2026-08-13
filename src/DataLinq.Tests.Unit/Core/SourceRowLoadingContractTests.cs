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
        await Assert.That(result.Rows[0]).IsSameReferenceAs(row);
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
        await Assert.That(result.Rows[0]).IsSameReferenceAs(backendMatchedRow);
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
    public async Task RowLoaderContract_UsesOwnedRequestResultAndCarriesCancellation()
    {
        var method = typeof(ISourceRowLoader).GetMethods().Single();
        var parameter = method.GetParameters().Single();

        await Assert.That(method.Name).IsEqualTo(nameof(ISourceRowLoader.Load));
        await Assert.That(method.ReturnType).IsEqualTo(typeof(SourceRowLoadResult));
        await Assert.That(parameter.ParameterType).IsEqualTo(typeof(SourcePrimaryKeyRowRequest));
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

        var exception = Capture<OperationCanceledException>(() => loader.Load(request));

        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
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

    private sealed class RecordingLoader : ISourceRowLoader
    {
        public bool BackendWorkStarted { get; private set; }

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
