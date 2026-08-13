using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class NullabilityMismatchTests
{
    private const string FullRowSql =
        "SELECT id, schema_required_text, model_required_text, required_number, optional_number " +
        "FROM nullability_contract_rows";

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ModelLoadingPaths_RejectSqlNullWithStableContextAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = CreateDatabase(provider, nameof(ModelLoadingPaths_RejectSqlNullWithStableContextAcrossProviders));
        var database = databaseScope.Database;
        var readOnly = database.Provider.ReadOnlyAccess;
        var table = database.Provider.Metadata.GetTableModel(typeof(NullabilityContractRow)).Table;
        var modelRequiredColumn = table.GetColumnByDbName("model_required_text");

        await Assert.That(modelRequiredColumn.Nullable).IsTrue();
        await Assert.That(modelRequiredColumn.ValueProperty.CsNullable).IsFalse();

        database.Provider.State.ClearCache();
        var canonicalDatabaseMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
            database.Query().Rows.Single(row => row.Id == 1));
        AssertMismatch(
            canonicalDatabaseMismatch,
            DataLinqNullabilityMismatchKind.DatabaseColumn,
            "schema_required_text",
            "SchemaRequiredText",
            $"sql:{provider.DatabaseType}");

        database.Provider.State.ClearCache();
        var canonicalModelMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
            database.Query().Rows.Single(row => row.Id == 2));
        AssertMismatch(
            canonicalModelMismatch,
            DataLinqNullabilityMismatchKind.ModelProperty,
            "model_required_text",
            "ModelRequiredText",
            $"sql:{provider.DatabaseType}");

        var rawQueryMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
            readOnly.GetFromQuery<NullabilityContractRow>($"{FullRowSql} WHERE id = 1").Single());
        AssertMismatch(
            rawQueryMismatch,
            DataLinqNullabilityMismatchKind.DatabaseColumn,
            "schema_required_text",
            "SchemaRequiredText",
            $"sql:{provider.DatabaseType}:raw-query");

        using (var rawCommand = new Literal(readOnly, $"{FullRowSql} WHERE id = 1").ToDbCommand())
        {
            var rawCommandMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
                readOnly.GetFromCommand<NullabilityContractRow>(rawCommand).Single());
            AssertMismatch(
                rawCommandMismatch,
                DataLinqNullabilityMismatchKind.DatabaseColumn,
                "schema_required_text",
                "SchemaRequiredText",
                $"sql:{provider.DatabaseType}:raw-command");
        }

        var selectMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
            database.From<NullabilityContractRow>()
                .Where("id").EqualTo(2)
                .SelectQuery()
                .ReadFirstRow());
        AssertMismatch(
            selectMismatch,
            DataLinqNullabilityMismatchKind.ModelProperty,
            "model_required_text",
            "ModelRequiredText",
            $"sql:{provider.DatabaseType}:select-first-row");

        using (var transaction = database.Transaction(TransactionType.ReadOnly))
        {
            var transactionQueryMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
                transaction.GetFromQuery<NullabilityContractRow>($"{FullRowSql} WHERE id = 1").Single());
            AssertMismatch(
                transactionQueryMismatch,
                DataLinqNullabilityMismatchKind.DatabaseColumn,
                "schema_required_text",
                "SchemaRequiredText",
                $"sql:{provider.DatabaseType}:transaction-query");
        }

        using (var transaction = database.Transaction(TransactionType.ReadOnly))
        using (var transactionCommand = new Literal(transaction, $"{FullRowSql} WHERE id = 1").ToDbCommand())
        {
            var transactionCommandMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
                transaction.GetFromCommand<NullabilityContractRow>(transactionCommand).Single());
            AssertMismatch(
                transactionCommandMismatch,
                DataLinqNullabilityMismatchKind.DatabaseColumn,
                "schema_required_text",
                "SchemaRequiredText",
                $"sql:{provider.DatabaseType}:transaction-command");
        }

        database.Provider.State.ClearCache();
        var valid = database.Query().Rows.Single(row => row.Id == 3);
        await Assert.That(valid.SchemaRequiredText).IsEqualTo("valid-schema");
        await Assert.That(valid.ModelRequiredText).IsEqualTo("valid-model");
        await Assert.That(valid.RequiredNumber).IsEqualTo(3);
        await Assert.That(valid.OptionalNumber).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DirectReaderAndProjection_RejectNullWithoutProducingClrDefaults(
        TestProviderDescriptor provider)
    {
        using var databaseScope = CreateDatabase(provider, nameof(DirectReaderAndProjection_RejectNullWithoutProducingClrDefaults));
        var database = databaseScope.Database;
        var table = database.Provider.Metadata.GetTableModel(typeof(NullabilityContractRow)).Table;
        var optionalColumn = table.GetColumnByDbName("optional_number");
        var requiredNumberColumn = table.GetColumnByDbName("required_number");
        var modelRequiredColumn = table.GetColumnByDbName("model_required_text");

        using (var reader = database.Provider.DatabaseAccess.ExecuteReader(
            "SELECT optional_number FROM nullability_contract_rows WHERE id = 3"))
        {
            await Assert.That(reader.ReadNextRow()).IsTrue();
            await Assert.That(reader.GetValue<int?>(optionalColumn, 0)).IsNull();
            await Assert.That(reader.GetValue<object>(optionalColumn, 0)).IsNull();

            var requestedTypeMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
                reader.GetValue<int>(optionalColumn, 0));
            AssertMismatch(
                requestedTypeMismatch,
                DataLinqNullabilityMismatchKind.RequestedClrType,
                "optional_number",
                "OptionalNumber",
                $"reader:{provider.DatabaseType}");
            await Assert.That(requestedTypeMismatch.ExpectedClrType).IsEqualTo(typeof(int));
        }

        using (var reader = database.Provider.DatabaseAccess.ExecuteReader(
            "SELECT model_required_text FROM nullability_contract_rows WHERE id = 2"))
        {
            await Assert.That(reader.ReadNextRow()).IsTrue();
            var modelMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
                reader.GetValue<string>(modelRequiredColumn, 0));
            AssertMismatch(
                modelMismatch,
                DataLinqNullabilityMismatchKind.ModelProperty,
                "model_required_text",
                "ModelRequiredText",
                $"reader:{provider.DatabaseType}");
        }

        using (var reader = database.Provider.DatabaseAccess.ExecuteReader(
            "SELECT required_number FROM nullability_contract_rows WHERE id = 4"))
        {
            await Assert.That(reader.ReadNextRow()).IsTrue();
            var databaseMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
                reader.GetValue<int>(requiredNumberColumn, 0));
            AssertMismatch(
                databaseMismatch,
                DataLinqNullabilityMismatchKind.DatabaseColumn,
                "required_number",
                "RequiredNumber",
                $"reader:{provider.DatabaseType}");
        }

        var projectionMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
            database.Query().Rows
                .Where(row => row.Id == 4)
                .Select(row => row.RequiredNumber)
                .Single());
        AssertMismatch(
            projectionMismatch,
            DataLinqNullabilityMismatchKind.DatabaseColumn,
            "required_number",
            "RequiredNumber",
            $"sql:{provider.DatabaseType}:scalar-projection");

        var constructorProjectionMismatch = Capture<DataLinqNullabilityMismatchException>(() =>
            database.Query().Rows
                .Where(row => row.Id == 4)
                .Select(row => new NullabilityNumberProjection(row.RequiredNumber))
                .Single());
        AssertMismatch(
            constructorProjectionMismatch,
            DataLinqNullabilityMismatchKind.DatabaseColumn,
            "required_number",
            "RequiredNumber",
            $"sql:{provider.DatabaseType}:row-projection");

        await Assert.That(projectionMismatch.Message).DoesNotContain(databaseScope.Connection.ConnectionString);
        await Assert.That(projectionMismatch.Message).DoesNotContain(databaseScope.Connection.DataSourceName);
        await Assert.That(constructorProjectionMismatch.Message).DoesNotContain(databaseScope.Connection.ConnectionString);
        await Assert.That(constructorProjectionMismatch.Message).DoesNotContain(databaseScope.Connection.DataSourceName);
    }

    private static TemporaryModelTestDatabase<NullabilityContractDb> CreateDatabase(
        TestProviderDescriptor provider,
        string scenarioName)
    {
        var scope = TemporaryModelTestDatabase<NullabilityContractDb>.Create(provider, scenarioName);
        try
        {
            var access = scope.Database.Provider.DatabaseAccess;
            access.ExecuteNonQuery("DROP TABLE nullability_contract_rows");
            access.ExecuteNonQuery(
                "CREATE TABLE nullability_contract_rows (" +
                "id INTEGER NOT NULL PRIMARY KEY, " +
                "schema_required_text VARCHAR(64) NULL, " +
                "model_required_text VARCHAR(64) NULL, " +
                "required_number INTEGER NULL, " +
                "optional_number INTEGER NULL)");
            access.ExecuteNonQuery(
                "INSERT INTO nullability_contract_rows " +
                "(id, schema_required_text, model_required_text, required_number, optional_number) VALUES " +
                "(1, NULL, 'model-one', 1, NULL), " +
                "(2, 'schema-two', NULL, 2, NULL), " +
                "(3, 'valid-schema', 'valid-model', 3, NULL), " +
                "(4, 'schema-four', 'model-four', NULL, NULL)");
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static void AssertMismatch(
        DataLinqNullabilityMismatchException exception,
        DataLinqNullabilityMismatchKind kind,
        string columnName,
        string propertyName,
        string sourceName)
    {
        if (exception.MismatchKind != kind ||
            exception.TableName != "nullability_contract_rows" ||
            exception.ColumnName != columnName ||
            exception.PropertyName != propertyName ||
            exception.ModelName != nameof(NullabilityContractRow) ||
            exception.SourceName != sourceName)
        {
            throw new Exception(
                $"Unexpected nullability mismatch context: {exception.MismatchKind}, " +
                $"{exception.TableName}.{exception.ColumnName}, {exception.ModelName}.{exception.PropertyName}, " +
                $"source {exception.SourceName}.");
        }

        if (exception.Message.Contains("columnIndex", StringComparison.Ordinal) ||
            exception.Message.Contains("Value cannot be null", StringComparison.Ordinal))
        {
            throw new Exception($"Nullability diagnostic is misleading: {exception.Message}");
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

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }
}

public sealed record NullabilityNumberProjection(int RequiredNumber);

[UseCache]
[Database("nullabilitycontract")]
public sealed partial class NullabilityContractDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<NullabilityContractRow> Rows { get; } = new(dataSource);
}

[Table("nullability_contract_rows")]
public abstract partial class NullabilityContractRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<NullabilityContractRow, NullabilityContractDb>(rowData, dataSource),
      ITableModel<NullabilityContractDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("schema_required_text")]
    public abstract string SchemaRequiredText { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("model_required_text")]
    public abstract string ModelRequiredText { get; }

    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("required_number")]
    public abstract int RequiredNumber { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("optional_number")]
    public abstract int? OptionalNumber { get; }
}
