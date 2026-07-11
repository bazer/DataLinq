using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    private static CanonicalProviderValueRow CreateCanonicalRow(
        TableDefinition table,
        int id,
        string name)
    {
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = id;
        values[table.GetColumnByDbName("name").Index] = name;
        return CanonicalProviderValueRow.Create(table, values);
    }

    private static DatabaseDefinition CreateMetadata()
    {
        var draft = new MetadataDatabaseDraft(
            "SourceRowLoadingContractDb",
            new CsTypeDeclaration(typeof(SourceRowLoadingContractTests)))
        {
            TableModels =
            [
                CreateTableModel("Rows", "source_rows", typeof(SourceRowModel)),
                CreateTableModel("OtherRows", "other_source_rows", typeof(OtherSourceRowModel))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static MetadataTableModelDraft CreateTableModel(
        string propertyName,
        string tableName,
        Type modelType) =>
        new(
            propertyName,
            new MetadataModelDraft(new CsTypeDeclaration(modelType))
            {
                ValueProperties =
                [
                    new MetadataValuePropertyDraft(
                        "Id",
                        new CsTypeDeclaration(typeof(int)),
                        new MetadataColumnDraft("id") { PrimaryKey = true })
                    {
                        CsSize = sizeof(int)
                    },
                    new MetadataValuePropertyDraft(
                        "Name",
                        new CsTypeDeclaration(typeof(string)),
                        new MetadataColumnDraft("name"))
                ]
            },
            new MetadataTableDraft(tableName));

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
}
