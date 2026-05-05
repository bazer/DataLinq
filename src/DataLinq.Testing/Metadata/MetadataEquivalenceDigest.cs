using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Testing;

public static class MetadataEquivalenceDigest
{
    public static string[] Create(DatabaseDefinition database, bool includeDatabaseStorageName = true)
    {
        var dbName = includeDatabaseStorageName
            ? database.DbName
            : database.Name;
        var lines = new List<string>
        {
            $"database|name={database.Name}|db={dbName}|cs={Format(database.CsType)}|cache={database.UseCache}",
            $"database-cache-limits|{string.Join(",", database.CacheLimits.OrderBy(x => x.limitType).ThenBy(x => x.amount).Select(x => $"{x.limitType}:{x.amount}"))}",
            $"database-cache-cleanup|{string.Join(",", database.CacheCleanup.OrderBy(x => x.cleanupType).ThenBy(x => x.amount).Select(x => $"{x.cleanupType}:{x.amount}"))}",
            $"database-index-cache|{string.Join(",", database.IndexCache.OrderBy(x => x.indexCacheType).ThenBy(x => x.amount).Select(x => $"{x.indexCacheType}:{x.amount}"))}",
        };

        foreach (var tableModel in database.TableModels.OrderBy(x => x.Table.DbName, StringComparer.Ordinal))
        {
            lines.AddRange(CreateTableDigest(tableModel));
        }

        return lines.ToArray();
    }

    public static string CreateText(DatabaseDefinition database, bool includeDatabaseStorageName = true) =>
        string.Join(Environment.NewLine, Create(database, includeDatabaseStorageName));

    private static IEnumerable<string> CreateTableDigest(TableModel tableModel)
    {
        var table = tableModel.Table;
        var model = tableModel.Model;

        yield return $"table|{table.DbName}|type={table.Type}|model={Format(model.CsType)}|property={tableModel.CsPropertyName}|interface={model.ModelInstanceInterface?.Name}|cache={table.UseCache}|attributes={FormatModelAttributes(model)}|definition={FormatViewDefinition(table)}";

        foreach (var column in table.Columns.OrderBy(x => x.Index).ThenBy(x => x.DbName, StringComparer.Ordinal))
        {
            var property = column.ValueProperty;
            yield return $"column|{table.DbName}.{column.Index}.{column.DbName}|property={property.PropertyName}|type={property.CsType.Name}|nullable={property.CsNullable}|dbNullable={column.Nullable}|pk={column.PrimaryKey}|fk={column.ForeignKey}|auto={column.AutoIncrement}|dbTypes={FormatDbTypes(column)}|enum={FormatEnum(property)}|default={FormatDefault(property.GetDefaultAttribute())}|attributes={FormatValueAttributes(property)}";
        }

        foreach (var index in table.ColumnIndices.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Characteristic).ThenBy(x => x.Type))
        {
            yield return $"index|{table.DbName}.{index.Name}|characteristic={index.Characteristic}|type={index.Type}|columns={string.Join(",", index.Columns.Select(x => x.DbName))}";
        }

        foreach (var relation in model.RelationProperties.Values.OrderBy(x => x.PropertyName, StringComparer.Ordinal))
        {
            var relationPart = relation.RelationPart;
            yield return $"relation-property|{table.DbName}.{relation.PropertyName}|type={relation.CsType.Name}|nullable={relation.CsNullable}|part={relationPart?.Type}|constraint={relationPart?.Relation.ConstraintName}|onUpdate={relationPart?.Relation.OnUpdate}|onDelete={relationPart?.Relation.OnDelete}|index={relationPart?.ColumnIndex.Name}|columns={FormatRelationColumns(relationPart)}";
        }
    }

    private static string Format(CsTypeDeclaration declaration) =>
        string.IsNullOrWhiteSpace(declaration.Namespace)
            ? declaration.Name
            : $"{declaration.Namespace}.{declaration.Name}";

    private static string FormatDbTypes(ColumnDefinition column) =>
        string.Join(
            ",",
            column.DbTypes
                .OrderBy(x => x.DatabaseType)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => $"{x.DatabaseType}:{x.Name}:{x.Length}:{x.Decimals}:{x.Signed}"));

    private static string FormatEnum(ValueProperty property) =>
        property.EnumProperty.HasValue
            ? string.Join(",", property.EnumProperty.Value.CsEnumValues.Select(x => $"{x.name}:{x.value}"))
            : "";

    private static string FormatDefault(DefaultAttribute? attribute)
    {
        if (attribute == null)
            return "";

        if (attribute is DefaultSqlAttribute defaultSql)
            return $"{nameof(DefaultSqlAttribute)}:{defaultSql.DatabaseType}:{NormalizeSql(defaultSql.Expression)}";

        var value = attribute.Value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => attribute.Value.ToString()
        };

        return $"{attribute.GetType().Name}:{value}";
    }

    private static string FormatModelAttributes(ModelDefinition model)
    {
        var comments = model.Attributes
            .OfType<CommentAttribute>()
            .Select(x => $"{x.DatabaseType}:{x.Text}");
        var checks = model.Attributes
            .OfType<CheckAttribute>()
            .Select(x => $"{x.DatabaseType}:{x.Name}:{NormalizeSql(x.Expression)}");

        return string.Join(",", comments.Concat(checks).OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string FormatValueAttributes(ValueProperty property)
    {
        var comments = property.Attributes
            .OfType<CommentAttribute>()
            .Select(x => $"{x.DatabaseType}:{x.Text}");
        var foreignKeys = property.Attributes
            .OfType<ForeignKeyAttribute>()
            .Select(x => $"fk:{x.Name}:{x.Table}:{x.Column}:{x.OnUpdate}:{x.OnDelete}");

        return string.Join(",", comments.Concat(foreignKeys).OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string FormatViewDefinition(TableDefinition table) =>
        table is ViewDefinition view
            ? NormalizeSql(view.Definition)
            : "";

    private static string FormatRelationColumns(RelationPart? relationPart) =>
        relationPart == null
            ? ""
            : string.Join(",", relationPart.ColumnIndex.Columns.Select(x => x.DbName));

    private static string NormalizeSql(string? sql) =>
        sql == null
            ? ""
            : string.Join(" ", sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
