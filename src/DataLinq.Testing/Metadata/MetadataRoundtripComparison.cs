using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Testing;

public static class MetadataRoundtripComparison
{
    public static IReadOnlyList<string> CompareSupportedSubset(
        DatabaseDefinition expected,
        DatabaseDefinition actual,
        DatabaseType databaseType)
    {
        var differences = new List<string>();

        CompareTables(expected, actual, databaseType, differences);

        return differences;
    }

    private static void CompareTables(
        DatabaseDefinition expected,
        DatabaseDefinition actual,
        DatabaseType databaseType,
        List<string> differences)
    {
        var expectedTables = expected.TableModels
            .OrderBy(x => x.Table.DbName, StringComparer.Ordinal)
            .ToList();
        var actualTables = actual.TableModels
            .OrderBy(x => x.Table.DbName, StringComparer.Ordinal)
            .ToList();

        CompareSet(
            expectedTables.Select(x => x.Table.DbName),
            actualTables.Select(x => x.Table.DbName),
            "tables",
            differences);

        foreach (var expectedTableModel in expectedTables)
        {
            var expectedTable = expectedTableModel.Table;
            var actualTable = actualTables
                .FirstOrDefault(x => string.Equals(x.Table.DbName, expectedTable.DbName, StringComparison.Ordinal))
                ?.Table;

            if (actualTable == null)
                continue;

            var path = expectedTable.DbName;

            AddIfDifferent(differences, $"{path}.type", expectedTable.Type, actualTable.Type);
            AddIfDifferent(differences, $"{path}.comment", FormatComment(expectedTable.Model.Attributes, databaseType), FormatComment(actualTable.Model.Attributes, databaseType));
            CompareColumns(expectedTable, actualTable, databaseType, differences);
            CompareIndexes(expectedTable, actualTable, differences);
            CompareRelationProperties(expectedTable.Model, actualTable.Model, differences);

            if (expectedTable is ViewDefinition expectedView && actualTable is ViewDefinition actualView)
                AddIfDifferent(differences, $"{path}.viewDefinition", NormalizeSql(expectedView.Definition), NormalizeSql(actualView.Definition));
        }
    }

    private static void CompareColumns(
        TableDefinition expectedTable,
        TableDefinition actualTable,
        DatabaseType databaseType,
        List<string> differences)
    {
        var expectedColumns = expectedTable.Columns.OrderBy(x => x.Index).ToList();
        var actualColumns = actualTable.Columns.OrderBy(x => x.Index).ToList();

        CompareSequence(
            expectedColumns.Select(x => x.DbName),
            actualColumns.Select(x => x.DbName),
            $"{expectedTable.DbName}.columns",
            differences);

        foreach (var expectedColumn in expectedColumns)
        {
            var actualColumn = actualColumns.FirstOrDefault(x => string.Equals(x.DbName, expectedColumn.DbName, StringComparison.Ordinal));
            if (actualColumn == null)
                continue;

            var path = $"{expectedTable.DbName}.{expectedColumn.DbName}";
            AddIfDifferent(differences, $"{path}.propertyName", expectedColumn.ValueProperty.PropertyName, actualColumn.ValueProperty.PropertyName);
            AddIfDifferent(differences, $"{path}.csType", expectedColumn.ValueProperty.CsType.Name, actualColumn.ValueProperty.CsType.Name);
            AddIfDifferent(differences, $"{path}.csNullable", expectedColumn.ValueProperty.CsNullable, actualColumn.ValueProperty.CsNullable);
            AddIfDifferent(differences, $"{path}.nullable", expectedColumn.Nullable, actualColumn.Nullable);
            AddIfDifferent(differences, $"{path}.primaryKey", expectedColumn.PrimaryKey, actualColumn.PrimaryKey);
            AddIfDifferent(differences, $"{path}.foreignKey", expectedColumn.ForeignKey, actualColumn.ForeignKey);
            AddIfDifferent(differences, $"{path}.autoIncrement", expectedColumn.AutoIncrement, actualColumn.AutoIncrement);
            AddIfDifferent(differences, $"{path}.dbType", FormatDbType(expectedColumn.GetDbTypeFor(databaseType)), FormatDbType(actualColumn.GetDbTypeFor(databaseType)));
            AddIfDifferent(differences, $"{path}.default", FormatDefault(expectedColumn.ValueProperty.GetDefaultAttribute()), FormatDefault(actualColumn.ValueProperty.GetDefaultAttribute()));
            AddIfDifferent(differences, $"{path}.comment", FormatComment(expectedColumn.ValueProperty.Attributes, databaseType), FormatComment(actualColumn.ValueProperty.Attributes, databaseType));

            CompareForeignKeyAttributes(expectedColumn, actualColumn, differences);
        }
    }

    private static void CompareIndexes(TableDefinition expectedTable, TableDefinition actualTable, List<string> differences)
    {
        var expectedIndexes = expectedTable.ColumnIndices
            .Where(IsProviderRoundtripIndex)
            .Select(FormatIndex)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var actualIndexes = actualTable.ColumnIndices
            .Where(IsProviderRoundtripIndex)
            .Select(FormatIndex)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        CompareSequence(expectedIndexes, actualIndexes, $"{expectedTable.DbName}.indexes", differences);
    }

    private static void CompareForeignKeyAttributes(
        ColumnDefinition expectedColumn,
        ColumnDefinition actualColumn,
        List<string> differences)
    {
        var expectedAttributes = expectedColumn.ValueProperty.Attributes
            .OfType<ForeignKeyAttribute>()
            .Select(FormatForeignKeyAttribute)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var actualAttributes = actualColumn.ValueProperty.Attributes
            .OfType<ForeignKeyAttribute>()
            .Select(FormatForeignKeyAttribute)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        CompareSequence(expectedAttributes, actualAttributes, $"{expectedColumn.Table.DbName}.{expectedColumn.DbName}.foreignKeys", differences);
    }

    private static void CompareRelationProperties(
        ModelDefinition expectedModel,
        ModelDefinition actualModel,
        List<string> differences)
    {
        var expectedRelations = expectedModel.RelationProperties.Values
            .Select(FormatRelationProperty)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var actualRelations = actualModel.RelationProperties.Values
            .Select(FormatRelationProperty)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        CompareSequence(expectedRelations, actualRelations, $"{expectedModel.Table.DbName}.relations", differences);
    }

    private static bool IsProviderRoundtripIndex(ColumnIndex index) =>
        index.Characteristic != IndexCharacteristic.VirtualDataLinq &&
        index.Characteristic != IndexCharacteristic.ForeignKey;

    private static string FormatIndex(ColumnIndex index) =>
        $"{index.Name}|{index.Characteristic}|{index.Type}|{string.Join(",", index.Columns.Select(x => x.DbName))}";

    private static string FormatForeignKeyAttribute(ForeignKeyAttribute attribute) =>
        $"{attribute.Name}|{attribute.Table}|{attribute.Column}";

    private static string FormatRelationProperty(RelationProperty property)
    {
        var relationPart = property.RelationPart;
        var index = relationPart.ColumnIndex;

        return string.Join(
            "|",
            property.PropertyName,
            property.CsType.Name,
            relationPart.Type,
            relationPart.Relation.ConstraintName,
            index.Table.DbName,
            string.Join(",", index.Columns.Select(x => x.DbName)));
    }

    private static string? FormatDbType(DatabaseColumnType? type)
    {
        if (type == null)
            return null;

        return string.Join(
            "|",
            type.DatabaseType,
            type.Name.ToLowerInvariant(),
            type.Length?.ToString(CultureInfo.InvariantCulture) ?? "",
            type.Decimals?.ToString(CultureInfo.InvariantCulture) ?? "",
            type.Signed?.ToString(CultureInfo.InvariantCulture) ?? "");
    }

    private static string? FormatDefault(DefaultAttribute? attribute)
    {
        if (attribute == null)
            return null;

        var value = attribute.Value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => attribute.Value.ToString()
        };

        return $"{attribute.GetType().Name}|{value}|{attribute.CodeExpression}";
    }

    private static string? FormatComment(IEnumerable<Attribute> attributes, DatabaseType databaseType)
    {
        var comments = attributes.OfType<CommentAttribute>().ToList();
        var providerComments = comments
            .Where(x => x.DatabaseType == databaseType)
            .Select(x => x.Text)
            .ToList();
        var effectiveComments = providerComments.Count > 0
            ? providerComments
            : comments
                .Where(x => x.DatabaseType == DatabaseType.Default)
                .Select(x => x.Text)
                .ToList();

        return effectiveComments.Count == 0
            ? null
            : string.Join("\n", effectiveComments);
    }

    private static string? NormalizeSql(string? sql) =>
        sql == null
            ? null
            : string.Join(" ", sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void CompareSet(
        IEnumerable<string> expected,
        IEnumerable<string> actual,
        string path,
        List<string> differences)
    {
        var expectedSet = expected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var actualSet = actual.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        CompareSequence(expectedSet, actualSet, path, differences);
    }

    private static void CompareSequence<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string path,
        List<string> differences)
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();

        if (expectedArray.SequenceEqual(actualArray))
            return;

        differences.Add($"{path}: expected [{string.Join(", ", expectedArray)}], actual [{string.Join(", ", actualArray)}]");
    }

    private static void AddIfDifferent<T>(List<string> differences, string path, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
            return;

        differences.Add($"{path}: expected '{expected}', actual '{actual}'");
    }
}
