using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Metadata;

public class FileFactoryOptions
{
    public string NamespaceName { get; set; } = "Models";
    public string Tab { get; set; } = "    ";
    public bool UseRecords { get; set; } = true;
    //public bool UseCache { get; set; } = true;
    public bool UseFileScopedNamespaces { get; set; }
    public bool SeparateTablesAndViews { get; set; } = false;
    public List<string> Usings { get; set; } = new List<string> { "System", "DataLinq", "DataLinq.Interfaces", "DataLinq.Attributes" };
}

public class FileFactory
{
    private readonly FileFactoryOptions options;

    public FileFactory(FileFactoryOptions options)
    {
        this.options = options;
    }

    public IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseMetadata database)
    {
        var dbCsTypeName = database.TableModels.Any(x => x.Model.CsTypeName == database.CsTypeName)
            ? $"{database.CsTypeName}Db"
            : database.CsTypeName;

        yield return ($"{dbCsTypeName}.cs",
                FileHeader(options.NamespaceName, options.UseFileScopedNamespaces, options.Usings)
                .Concat(DatabaseFileContents(database, dbCsTypeName, options))
                .Concat(FileFooter(options.UseFileScopedNamespaces))
                .ToJoinedString("\n"));

        foreach (var table in database.TableModels)
        {
            var usings = options.Usings
                .Concat(table.Model.Namespaces?.Select(x => x.FullNamespaceName) ?? new List<string>())
                .Concat(table.Model.RelationProperties.Values
                    .Where(x => x.RelationPart.Type == RelationPartType.CandidateKey)
                    .Select(x => "System.Collections.Generic"))
                .Distinct()
                .Where(x => x != null)
                .Where(name => name != options.NamespaceName)
                .Select(name => (name.StartsWith("System"), name))
                .OrderByDescending(x => x.Item1)
                .ThenBy(x => x.name)
                .Select(x => x.name);

            var file =
                FileHeader(options.NamespaceName, options.UseFileScopedNamespaces, usings)
                .Concat(ModelFileContents(table.Model, options))
                .Concat(FileFooter(options.UseFileScopedNamespaces))
                .ToJoinedString("\n");

            var path = GetFilePath(table);

            yield return (path, file);
        }
    }

    private string GetFilePath(TableModelMetadata table)
    {
        var path = $"{table.Model.CsTypeName}.cs";

        if (options.SeparateTablesAndViews)
            return table.Table.Type == TableType.Table
                ? $"Tables{Path.DirectorySeparatorChar}{path}"
                : $"Views{Path.DirectorySeparatorChar}{path}";

        return path;
    }

    private IEnumerable<string> DatabaseFileContents(DatabaseMetadata database, string dbName, FileFactoryOptions settings)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = settings.Tab;

        if (database.UseCache)
            yield return $"{namespaceTab}[UseCache]";

        foreach (var limit in database.CacheLimits)
            yield return $"{namespaceTab}[CacheLimit(CacheLimitType.{limit.limitType}, {limit.amount})]";

        foreach (var cleanup in database.CacheCleanup)
            yield return $"{namespaceTab}[CacheCleanup(CacheCleanupType.{cleanup.cleanupType}, {cleanup.amount})]";

        yield return $"{namespaceTab}[Database(\"{database.Name}\")]";
        yield return $"{namespaceTab}public interface {dbName} : IDatabaseModel";
        yield return namespaceTab + "{";

        foreach (var t in database.TableModels.OrderBy(x => x.Table.DbName))
        {
            yield return $"{namespaceTab}{tab}DbRead<{t.Model.CsTypeName}> {t.CsPropertyName} {{ get; }}";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ModelFileContents(ModelMetadata model, FileFactoryOptions options)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;
        var table = model.Table;

        var valueProps = model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.CsName)
            .ToList();

        var relationProps = model.RelationProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.CsName)
            .ToList();

        foreach (var row in valueProps.Where(x => x.EnumProperty != null && !x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(x, namespaceTab, tab)))
            yield return row;

        if (table is ViewMetadata view)
        {
            yield return $"{namespaceTab}[Definition(\"{view.Definition}\")]";
            yield return $"{namespaceTab}[View(\"{table.DbName}\")]";
        }
        else
        {
            yield return $"{namespaceTab}[Table(\"{table.DbName}\")]";
        }

        var interfaces = table.Type == TableType.Table ? "ITableModel" : "IViewModel";

        interfaces += $"<{model.Database.CsTypeName}>";
        //if (model.Interfaces?.Length > 0)
        //    interfaces += ", " + model.Interfaces.Select(x => x.Name).ToJoinedString(", ");

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} {table.Model.CsTypeName} : {interfaces}";
        yield return namespaceTab + "{";

        foreach (var row in valueProps.Where(x => x.EnumProperty != null && x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(x, namespaceTab, tab)))
            yield return tab + row;

        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;
            if (c.PrimaryKey)
                yield return $"{namespaceTab}{tab}[PrimaryKey]";

            foreach (var index in c.ColumnIndices.Where(x => x.Characteristic != IndexCharacteristic.PrimaryKey && x.Characteristic != IndexCharacteristic.ForeignKey && x.Characteristic != IndexCharacteristic.VirtualDataLinq))
            {
                var columns = index.Columns.Count() > 1
                    ? "," + index.Columns.Select(x => $"\"{x.DbName}\"").ToJoinedString(", ")
                    : string.Empty;

                yield return $"{namespaceTab}{tab}[Index(\"{index.Name}\", IndexCharacteristic.{index.Characteristic}, IndexType.{index.Type}{columns})]";
            }

            foreach (var index in c.ColumnIndices)
            {
                foreach (var relationPart in index.RelationParts.Where(x => x.Type == RelationPartType.ForeignKey))
                {
                    yield return $"{namespaceTab}{tab}[ForeignKey(\"{relationPart.Relation.CandidateKey.ColumnIndex.Table.DbName}\", \"{relationPart.Relation.CandidateKey.ColumnIndex.Columns[0].DbName}\", \"{relationPart.Relation.ConstraintName}\")]";
                }
            }

            if (c.AutoIncrement)
                yield return $"{namespaceTab}{tab}[AutoIncrement]";

            if (c.Nullable)
                yield return $"{namespaceTab}{tab}[Nullable]";

            foreach (var dbType in c.DbTypes.OrderBy(x => x.DatabaseType))
            {
                if (dbType.Signed.HasValue && dbType.Decimals.HasValue && dbType.Length.HasValue)
                    yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length}, {dbType.Decimals}, {(dbType.Signed.Value ? "true" : "false")})]";
                else if (dbType.Signed.HasValue && dbType.Length.HasValue)
                    yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length}, {(dbType.Signed.Value ? "true" : "false")})]";
                else if (dbType.Signed.HasValue && !dbType.Length.HasValue)
                    yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {(dbType.Signed.Value ? "true" : "false")})]";
                else if (dbType.Length.HasValue && dbType.Decimals.HasValue)
                    yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length}, {dbType.Decimals})]";
                else if (dbType.Length.HasValue)
                    yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length})]";
                else
                    yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\")]";
            }

            if (valueProperty.EnumProperty != null)
                yield return $"{namespaceTab}{tab}[Enum({string.Join(", ", valueProperty.EnumProperty.Value.EnumValues.Select(x => $"\"{x.name}\""))})]";

            yield return $"{namespaceTab}{tab}[Column(\"{c.DbName}\")]";
            yield return $"{namespaceTab}{tab}public virtual {c.ValueProperty.CsTypeName}{(c.ValueProperty.CsNullable || c.AutoIncrement ? "?" : "")} {c.ValueProperty.CsName} {{ get; set; }}";
            yield return $"";
        }

        foreach (var relationProperty in relationProps)
        {
            var otherPart = relationProperty.RelationPart.GetOtherSide();

            if (otherPart.ColumnIndex.Columns.Count == 1)
                yield return $"{namespaceTab}{tab}[Relation(\"{otherPart.ColumnIndex.Table.DbName}\", \"{otherPart.ColumnIndex.Columns[0].DbName}\", \"{relationProperty.RelationName}\")]";
            else
                yield return $"{namespaceTab}{tab}[Relation(\"{otherPart.ColumnIndex.Table.DbName}\", [{otherPart.ColumnIndex.Columns.Select(x => $"\"{x.DbName}\"").ToJoinedString(", ")}], \"{relationProperty.RelationName}\")]";

            if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
                yield return $"{namespaceTab}{tab}public virtual {otherPart.ColumnIndex.Table.Model.CsTypeName} {relationProperty.CsName} {{ get; }}";
            else
                yield return $"{namespaceTab}{tab}public virtual IEnumerable<{otherPart.ColumnIndex.Table.Model.CsTypeName}> {relationProperty.CsName} {{ get; }}";

            yield return $"";
        }


        yield return namespaceTab + "}";
    }

    private IEnumerable<string> WriteEnum(ValueProperty property, string namespaceTab, string tab)
    {
        yield return $"{namespaceTab}public enum {property.CsTypeName}";
        yield return namespaceTab + "{";
        //yield return $"{tab}{tab}Empty,";

        foreach (var val in property.EnumProperty.Value.EnumValues)
            yield return $"{namespaceTab}{tab}{val.name} = {val.value},";

        yield return namespaceTab + "}";
        yield return "";
    }

    private IEnumerable<string> FileHeader(string namespaceName, bool useFileScopedNamespaces, IEnumerable<string> usings)
    {
        foreach (var row in usings)
            yield return $"using {row};";

        yield return "";
        yield return $"namespace {namespaceName}{(useFileScopedNamespaces ? ";" : "")}";


        if (useFileScopedNamespaces)
            yield return "";
        else
            yield return "{";
    }

    private IEnumerable<string> FileFooter(bool useFileScopedNamespaces)
    {
        if (!useFileScopedNamespaces)
            yield return "}";
    }
}
