using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class StringWhitespaceTranslationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task IsNullOrWhiteSpace_MatchesClrForEveryWhitespaceCharacterAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<StringWhitespaceDb>.Create(
            provider,
            nameof(IsNullOrWhiteSpace_MatchesClrForEveryWhitespaceCharacterAcrossProviders));
        var database = databaseScope.Database;
        var values = CreateTestValues();
        var parameterSign = database.Provider.Constants.ParameterSign;
        using var parameterConnection = database.Provider.GetDbConnection();
        using var insertCommand = parameterConnection.CreateCommand();
        insertCommand.CommandText =
            $"INSERT INTO string_whitespace_rows (id, text_value) VALUES ({parameterSign}id, {parameterSign}textValue)";
        var idParameter = insertCommand.CreateParameter();
        idParameter.ParameterName = $"{parameterSign}id";
        insertCommand.Parameters.Add(idParameter);
        var textParameter = insertCommand.CreateParameter();
        textParameter.ParameterName = $"{parameterSign}textValue";
        insertCommand.Parameters.Add(textParameter);
        var inserted = 0;

        foreach (var (value, index) in values.Select(static (value, index) => (value, index)))
        {
            idParameter.Value = index + 1;
            textParameter.Value = (object?)value ?? DBNull.Value;
            inserted += database.Provider.DatabaseAccess.ExecuteNonQuery(insertCommand);
        }

        var materialized = database.Query().Rows.ToList();
        var expectedWhitespace = materialized
            .Where(static row => string.IsNullOrWhiteSpace(row.Value))
            .Select(static row => row.Id)
            .OrderBy(static id => id)
            .ToArray();
        var expectedNonWhitespace = materialized
            .Where(static row => !string.IsNullOrWhiteSpace(row.Value))
            .Select(static row => row.Id)
            .OrderBy(static id => id)
            .ToArray();
        var actualWhitespace = database.Query().Rows
            .Where(static row => string.IsNullOrWhiteSpace(row.Value))
            .Select(static row => row.Id)
            .OrderBy(static id => id)
            .ToArray();
        var actualNonWhitespace = database.Query().Rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Value))
            .Select(static row => row.Id)
            .OrderBy(static id => id)
            .ToArray();

        await Assert.That(inserted).IsEqualTo(values.Length);
        await Assert.That(actualWhitespace).IsEquivalentTo(expectedWhitespace);
        await Assert.That(actualNonWhitespace).IsEquivalentTo(expectedNonWhitespace);
        await Assert.That(actualWhitespace.Length + actualNonWhitespace.Length).IsEqualTo(values.Length);
    }

    [Test]
    public async Task IsNullOrWhiteSpace_SqlUsesTheCompleteClrSetAndNulSafeByteLength()
    {
        using var databaseScope = TemporaryModelTestDatabase<StringWhitespaceDb>.Create(
            TestProviderMatrix.SQLiteInMemory,
            nameof(IsNullOrWhiteSpace_SqlUsesTheCompleteClrSetAndNulSafeByteLength));
        var positive = CurrentQueryTranslationInspection.BuildSql(
            databaseScope.Database,
            databaseScope.Database.Query().Rows.Where(static row => string.IsNullOrWhiteSpace(row.Value)));
        var negated = CurrentQueryTranslationInspection.BuildSql(
            databaseScope.Database,
            databaseScope.Database.Query().Rows.Where(static row => !string.IsNullOrWhiteSpace(row.Value)));
        var clrWhitespaceCharacters = new string(Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Select(static value => (char)value)
            .Where(char.IsWhiteSpace)
            .ToArray());

        await Assert.That(clrWhitespaceCharacters.Length).IsEqualTo(25);
        await Assert.That(QueryPlanSqlValueRenderer.ClrWhitespaceCharacters).IsEqualTo(clrWhitespaceCharacters);
        await Assert.That(CountOccurrences(positive.Text, "REPLACE(")).IsEqualTo(clrWhitespaceCharacters.Length);
        await Assert.That(CountOccurrences(negated.Text, "REPLACE(")).IsEqualTo(clrWhitespaceCharacters.Length);
        await Assert.That(positive.Text).Contains("LENGTH(CAST(");
        await Assert.That(negated.Text).Contains("LENGTH(CAST(");
        await Assert.That(positive.Text).Contains("AS BLOB)");
        await Assert.That(negated.Text).Contains("AS BLOB)");
        await Assert.That(positive.Text).DoesNotContain("TRIM(");
        await Assert.That(negated.Text).DoesNotContain("TRIM(");
    }

    private static string?[] CreateTestValues()
    {
        var clrWhitespace = Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Select(static value => (char)value)
            .Where(char.IsWhiteSpace)
            .Select(static value => value.ToString());

        return
        [
            null,
            string.Empty,
            .. clrWhitespace,
            "\t \u00A0\u2003\r\n",
            "text",
            " text ",
            "\u2003text\u2003",
            "\0",
            "\0text",
            "\u180E",
            "\u200B",
            "\uFEFF"
        ];
    }

    private static int CountOccurrences(string value, string search) =>
        value.Split(search, StringSplitOptions.None).Length - 1;
}

[Database("stringwhitespace")]
public sealed partial class StringWhitespaceDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<StringWhitespaceRow> Rows { get; } = new(dataSource);
}

[Table("string_whitespace_rows")]
public abstract partial class StringWhitespaceRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<StringWhitespaceRow, StringWhitespaceDb>(rowData, dataSource),
      ITableModel<StringWhitespaceDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("text_value")]
    public abstract string? Value { get; }
}
