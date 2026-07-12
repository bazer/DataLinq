using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class ModelValueConverterTests
{
    [Test]
    public async Task StateChange_InsertWriteSlots_DistinguishUnsetFromAssignedNull()
    {
        var table = CreateDefaultSlotTable();
        var defaultColumn = table.GetColumnByDbName("server_value");
        var unset = CreateDefaultSlotStateChange(table, assignDefault: false, defaultValue: null);
        var assignedNull = CreateDefaultSlotStateChange(table, assignDefault: true, defaultValue: null);

        var unsetSlots = unset.GetInsertWriteSlots();
        var unsetSlot = unsetSlots.Single(slot => ReferenceEquals(slot.Column, defaultColumn));
        var assignedNullSlot = assignedNull.GetInsertWriteSlots().Single(slot => ReferenceEquals(slot.Column, defaultColumn));

        await Assert.That(unsetSlots.Count).IsEqualTo(table.ColumnCount);
        for (var index = 0; index < unsetSlots.Count; index++)
            await Assert.That(unsetSlots[index].Column).IsSameReferenceAs(table.Columns[index]);

        await Assert.That(unsetSlot.IsAssigned).IsFalse();
        await Assert.That(unsetSlot.ModelValue).IsNull();
        await Assert.That(assignedNullSlot.IsAssigned).IsTrue();
        await Assert.That(assignedNullSlot.ModelValue).IsNull();
    }

    [Test]
    public async Task StateChange_Insert_OmitsEligibleUnsetDefaultButWritesAssignedNull()
    {
        var table = CreateDefaultSlotTable();
        var idColumn = table.GetColumnByDbName("id");
        var defaultColumn = table.GetColumnByDbName("server_value");

        var unsetWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(table, assignDefault: false, defaultValue: null));
        var assignedNullWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(table, assignDefault: true, defaultValue: null));
        var assignedValueWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(table, assignDefault: true, defaultValue: "client-value"));

        await Assert.That(unsetWriter.Calls.Count).IsEqualTo(1);
        await Assert.That(unsetWriter.Calls.Single().Column).IsSameReferenceAs(idColumn);
        await Assert.That(assignedNullWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(assignedNullWriter.Calls[0].Column).IsSameReferenceAs(idColumn);
        await Assert.That(assignedNullWriter.Calls[1].Column).IsSameReferenceAs(defaultColumn);
        await Assert.That(assignedNullWriter.Calls[1].CanonicalValue).IsNull();
        await Assert.That(assignedValueWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(assignedValueWriter.Calls[1].Column).IsSameReferenceAs(defaultColumn);
        await Assert.That(assignedValueWriter.Calls[1].CanonicalValue).IsEqualTo("client-value");
    }

    [Test]
    public async Task StateChange_Insert_DefaultOmissionPreservesUnsafeWritesAndSupportsConvertedValues()
    {
        var providerMismatchTable = CreateDefaultSlotTable(DatabaseType.MySQL);
        var exactProviderTable = CreateDefaultSlotTable(DatabaseType.SQLite);
        var indexedTable = CreateDefaultSlotTable(indexed: true);
        var clientDefaultTable = CreateDefaultSlotTable(clientDefault: true);
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value);
        var convertedTable = CreateDefaultSlotTable(converter: converter);
        var convertedDefaultWithStringKeyTable = CreateDefaultSlotTable(
            converter: converter,
            primaryKeyType: typeof(string));
        var primaryKeyConverter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value);
        var convertedPrimaryKeyTable = CreateDefaultSlotTable(primaryKeyConverter: primaryKeyConverter);
        var autoIncrementPrimaryKeyConverter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value);
        var convertedAutoIncrementTable = CreateDefaultSlotTable(
            primaryKeyConverter: autoIncrementPrimaryKeyConverter,
            autoIncrement: true);
        var nonIntegralAutoIncrementTable = CreateDefaultSlotTable(
            primaryKeyType: typeof(string),
            autoIncrement: true);
        var unknownKeyTable = CreateDefaultSlotTable();
        var autoIncrementTable = CreateDefaultSlotTable(autoIncrement: true);

        var providerMismatchWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(providerMismatchTable, assignDefault: false, defaultValue: null));
        var exactProviderWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(exactProviderTable, assignDefault: false, defaultValue: null));
        var indexedWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(indexedTable, assignDefault: false, defaultValue: null));
        var clientDefaultWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(clientDefaultTable, assignDefault: false, defaultValue: null));
        var convertedWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(convertedTable, assignDefault: false, defaultValue: null));
        var convertedDefaultWithStringKeyWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                convertedDefaultWithStringKeyTable,
                assignDefault: false,
                defaultValue: null,
                primaryKeyValue: "known-key"));
        var convertedPrimaryKeyWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                convertedPrimaryKeyTable,
                assignDefault: false,
                defaultValue: null,
                primaryKeyValue: new MutationId(7)));
        var unknownKeyWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                unknownKeyTable,
                assignDefault: false,
                defaultValue: null,
                hasPrimaryKey: false));
        var unsetAutoIncrementWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                autoIncrementTable,
                assignDefault: false,
                defaultValue: null,
                hasPrimaryKey: false,
                assignPrimaryKey: false));
        var unsupportedDefaultOnlyInsertWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                autoIncrementTable,
                assignDefault: false,
                defaultValue: null,
                hasPrimaryKey: false,
                assignPrimaryKey: false),
            supportsDefaultOnlyInsert: false);
        var assignedNullAutoIncrementWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                autoIncrementTable,
                assignDefault: false,
                defaultValue: null,
                hasPrimaryKey: false));
        var explicitAutoIncrementWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                autoIncrementTable,
                assignDefault: false,
                defaultValue: null,
                primaryKeyValue: 19));
        var convertedAutoIncrementWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                convertedAutoIncrementTable,
                assignDefault: false,
                defaultValue: null,
                hasPrimaryKey: false,
                assignPrimaryKey: false));
        var nonIntegralAutoIncrementWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                nonIntegralAutoIncrementTable,
                assignDefault: false,
                defaultValue: null,
                hasPrimaryKey: false,
                assignPrimaryKey: false));
        var untrackedValueWriter = BuildInsertAndCaptureWriter(
            CreateDefaultSlotStateChange(
                CreateDefaultSlotTable(),
                assignDefault: false,
                defaultValue: "untracked-value"));

        await Assert.That(providerMismatchWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(exactProviderWriter.Calls.Count).IsEqualTo(1);
        await Assert.That(indexedTable.GetColumnByDbName("server_value").ColumnIndices.Any()).IsTrue();
        await Assert.That(indexedWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(clientDefaultWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(convertedWriter.Calls.Count).IsEqualTo(1);
        await Assert.That(convertedWriter.Calls.Single().Column)
            .IsSameReferenceAs(convertedTable.GetColumnByDbName("id"));
        await Assert.That(convertedDefaultWithStringKeyWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(convertedDefaultWithStringKeyWriter.Calls[1].Column)
            .IsSameReferenceAs(convertedDefaultWithStringKeyTable.GetColumnByDbName("server_value"));
        await Assert.That(converter.ToProviderCalls).IsEmpty();
        await Assert.That(convertedPrimaryKeyWriter.Calls.Count).IsEqualTo(1);
        await Assert.That(convertedPrimaryKeyWriter.Calls.Single().Column)
            .IsSameReferenceAs(convertedPrimaryKeyTable.GetColumnByDbName("id"));
        // Capture, deferred-insert identity validation, and insert serialization each cross
        // the model-to-canonical boundary deliberately.
        await Assert.That(primaryKeyConverter.ToProviderCalls.Count).IsEqualTo(3);
        await Assert.That(unknownKeyWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(autoIncrementTable.HasAutoIncrementPrimaryKey).IsTrue();
        await Assert.That(unsetAutoIncrementWriter.Calls).IsEmpty();
        await Assert.That(unsupportedDefaultOnlyInsertWriter.Calls.Count).IsEqualTo(1);
        await Assert.That(unsupportedDefaultOnlyInsertWriter.Calls.Single().Column)
            .IsSameReferenceAs(autoIncrementTable.AutoIncrementPrimaryKeyColumn);
        await Assert.That(unsupportedDefaultOnlyInsertWriter.Calls.Single().CanonicalValue).IsNull();
        await Assert.That(assignedNullAutoIncrementWriter.Calls.Count).IsEqualTo(1);
        await Assert.That(assignedNullAutoIncrementWriter.Calls.Single().Column)
            .IsSameReferenceAs(autoIncrementTable.AutoIncrementPrimaryKeyColumn);
        await Assert.That(assignedNullAutoIncrementWriter.Calls.Single().CanonicalValue).IsNull();
        await Assert.That(explicitAutoIncrementWriter.Calls.Count).IsEqualTo(1);
        await Assert.That(explicitAutoIncrementWriter.Calls.Single().Column)
            .IsSameReferenceAs(autoIncrementTable.AutoIncrementPrimaryKeyColumn);
        await Assert.That(explicitAutoIncrementWriter.Calls.Single().CanonicalValue).IsEqualTo(19);
        await Assert.That(convertedAutoIncrementWriter.Calls).IsEmpty();
        await Assert.That(autoIncrementPrimaryKeyConverter.ToProviderCalls).IsEmpty();
        await Assert.That(nonIntegralAutoIncrementWriter.Calls.Count).IsEqualTo(2);
        await Assert.That(untrackedValueWriter.Calls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task InsertQuery_ProviderWithoutDefaultOnlyCapabilityPreservesLegacyEmptyShape()
    {
        var table = CreateDefaultSlotTable(autoIncrement: true);
        var writer = new RecordingPhysicalWriter();
        var provider = new RecordingProvider(
            table.Database,
            writer,
            supportsDefaultOnlyInsert: false);
        var transaction = new Transaction(provider, TransactionType.WriteOnly);

        var sql = new SqlQuery(table, transaction).InsertQuery().ToSql();

        await Assert.That(sql.Text)
            .IsEqualTo($"INSERT INTO {table.DbName} VALUES (NULL)");
        await Assert.That(sql.Parameters).IsEmpty();
    }

    private static TableDefinition CreateDefaultSlotTable(
        DatabaseType defaultDatabaseType = DatabaseType.Default,
        bool indexed = false,
        bool clientDefault = false,
        RecordingScalarConverter? converter = null,
        RecordingScalarConverter? primaryKeyConverter = null,
        Type? primaryKeyType = null,
        bool autoIncrement = false)
    {
        var defaultAttributes = new List<Attribute>
        {
            clientDefault
                ? new DefaultAttribute("client-default")
                : new DefaultSqlAttribute(
                    defaultDatabaseType,
                    converter is null ? "'server'" : "17")
        };
        if (indexed)
            defaultAttributes.Add(new IndexAttribute("idx_default_slot_value", IndexCharacteristic.Simple));

        var defaultProperty = new MetadataValuePropertyDraft(
            "ServerValue",
            new CsTypeDeclaration(converter?.ModelType ?? typeof(string)),
            new MetadataColumnDraft("server_value") { Nullable = true })
        {
            Attributes = defaultAttributes,
            CsNullable = true,
            ScalarConverter = converter is null ? null : CreateConverterDraft(converter)
        };
        var draft = CreateDatabaseDraft(
            $"default_slot_rows_{defaultDatabaseType}_{indexed}_{clientDefault}_{converter is not null}_{primaryKeyConverter is not null}_{primaryKeyType?.Name}_{autoIncrement}",
            new MetadataValuePropertyDraft(
                "Id",
                new CsTypeDeclaration(primaryKeyConverter?.ModelType ?? primaryKeyType ?? typeof(int)),
                new MetadataColumnDraft("id")
                {
                    PrimaryKey = true,
                    AutoIncrement = autoIncrement
                })
            {
                ScalarConverter = primaryKeyConverter is null
                    ? null
                    : CreateConverterDraft(primaryKeyConverter)
            },
            defaultProperty);

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;
    }

    private static StateChange CreateDefaultSlotStateChange(
        TableDefinition table,
        bool assignDefault,
        object? defaultValue,
        bool hasPrimaryKey = true,
        object? primaryKeyValue = null,
        bool assignPrimaryKey = true)
    {
        var idColumn = table.GetColumnByDbName("id");
        var defaultColumn = table.GetColumnByDbName("server_value");
        primaryKeyValue = hasPrimaryKey ? primaryKeyValue ?? 7 : null;
        var values = new Dictionary<ColumnDefinition, object?>
        {
            [idColumn] = primaryKeyValue,
            [defaultColumn] = defaultValue
        };
        var changes = new List<KeyValuePair<ColumnDefinition, object?>>();
        if (assignPrimaryKey)
            changes.Add(new KeyValuePair<ColumnDefinition, object?>(idColumn, primaryKeyValue));
        if (assignDefault)
            changes.Add(new KeyValuePair<ColumnDefinition, object?>(defaultColumn, defaultValue));

        var model = new TestMutableInstance(table, values, changes, isNew: true);
        return new StateChange(model, table, TransactionChangeType.Insert);
    }

    private static RecordingPhysicalWriter BuildInsertAndCaptureWriter(
        StateChange stateChange,
        bool supportsDefaultOnlyInsert = true)
    {
        var writer = new RecordingPhysicalWriter();
        var provider = new RecordingProvider(
            stateChange.Table.Database,
            writer,
            supportsDefaultOnlyInsert: supportsDefaultOnlyInsert);
        var transaction = new Transaction(provider, TransactionType.WriteOnly);

        _ = stateChange.GetQuery(transaction);
        return writer;
    }
}
