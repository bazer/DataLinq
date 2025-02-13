using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;

namespace DataLinq.Core.Factories.Models;

public class ModelFileFactoryOptions
{
    public string? NamespaceName { get; set; } = null; //"Models";
    public string Tab { get; set; } = "    ";
    public bool UseRecords { get; set; } = true;
    //public bool UseCache { get; set; } = true;
    public bool UseFileScopedNamespaces { get; set; }
    public bool UseNullableReferenceTypes { get; set; }
    public bool SeparateTablesAndViews { get; set; } = false;
    public List<string> Usings { get; set; } = new List<string> { "System", "DataLinq", "DataLinq.Interfaces", "DataLinq.Attributes", "DataLinq.Instances", "DataLinq.Mutation" };
}

public enum ModelType
{
    classType,
    interfaceType
}

public class ModelFileFactory
{
    private readonly ModelFileFactoryOptions options;

    public ModelFileFactory(ModelFileFactoryOptions options)
    {
        this.options = options;
    }

    public IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseDefinition database)
    {
        //var dbCsTypeName = database.TableModels.Any(x => x.Model.CsTypeName == database.CsTypeName)
        //    ? $"I{database.CsTypeName}Db"
        //    : $"I{database.CsTypeName}";

        yield return ($"{database.CsType.Name}.cs",
                FileHeader(options.NamespaceName ?? database.CsType.Namespace, options.UseFileScopedNamespaces, options.Usings)
                .Concat(DatabaseFileContents(database, database.CsType.Name, options))
                .Concat(FileFooter(options.UseFileScopedNamespaces))
                .ToJoinedString("\n"));

        foreach (var table in database.TableModels.Where(x => !x.IsStub))
        {
            var namespaceName = options.NamespaceName ?? table.Model.CsType.Namespace;
            if (namespaceName == null)
                throw new Exception($"Namespace is null for '{table.Model.CsType.Name}'");

            var usings = options.Usings
                .Concat(table.Model.Usings?.Select(x => x.FullNamespaceName) ?? new List<string>())
                .Concat(table.Model.RelationProperties.Values
                    .Where(x => x.RelationPart.Type == RelationPartType.CandidateKey)
                    .Select(x => "System.Collections.Generic"))
                .Distinct()
                .Where(x => x != null)
                .Where(name => name != namespaceName)
                .Select(name => (name.StartsWith("System"), name))
                .OrderByDescending(x => x.Item1)
                .ThenBy(x => x.name)
                .Select(x => x.name);

            var file =
                FileHeader(namespaceName, options.UseFileScopedNamespaces, usings)
                .Concat(ModelFileContents(table.Model, options))
                .Concat(FileFooter(options.UseFileScopedNamespaces))
                .ToJoinedString("\n");

            var path = GetFilePath(table);

            yield return (path, file);
        }
    }

    private string GetFilePath(TableModel table)
    {
        var path = $"{table.Model.CsType.Name}.cs";

        if (options.SeparateTablesAndViews)
            return table.Table.Type == TableType.Table
                ? $"Tables{Path.DirectorySeparatorChar}{path}"
                : $"Views{Path.DirectorySeparatorChar}{path}";

        return path;
    }

    private IEnumerable<string> DatabaseFileContents(DatabaseDefinition database, string dbName, ModelFileFactoryOptions settings)
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
        yield return $"{namespaceTab}public partial class {dbName}(DataSourceAccess dataSource) : IDatabaseModel";
        //yield return $"{namespaceTab}public interface {dbName} : IDatabaseModel";
        yield return namespaceTab + "{";

        foreach (var t in database.TableModels.OrderBy(x => x.Table.DbName))
        {
            yield return $"{namespaceTab}{tab}public DbRead<{t.Model.CsType.Name}> {t.CsPropertyName} {{ get; }} = new DbRead<{t.Model.CsType.Name}>(dataSource);";
            //yield return $"{namespaceTab}{tab}DbRead<I{t.Model.CsTypeName}> {t.CsPropertyName} {{ get; }}";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ModelFileContents(ModelDefinition model, ModelFileFactoryOptions options)
    {
        var valueProps = model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .ToList();

        var relationProps = model.RelationProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .ToList();

        foreach (var row in valueProps.Where(x => x.EnumProperty != null && !x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(options, x)))
            yield return row;
        
        
        
        foreach (var row in WriteClass(model, options, valueProps, relationProps))
            yield return row;
    }

    

    private IEnumerable<string> WriteClass(ModelDefinition model, ModelFileFactoryOptions options, List<ValueProperty> valueProps, List<RelationProperty> relationProps)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;
        var table = model.Table;

        if (table is ViewDefinition view)
        {
            yield return $"{namespaceTab}[Definition(\"{view.Definition}\")]";
            yield return $"{namespaceTab}[View(\"{table.DbName}\")]";
        }
        else
        {
            yield return $"{namespaceTab}[Table(\"{table.DbName}\")]";
        }

        var interfaces = table.Type == TableType.Table ? "ITableModel" : "IViewModel";

        interfaces += $"<{model.Database.CsType.Name}>";
        //interfaces += $", I{table.Model.CsType.Name}";
        //if (model.Interfaces?.Length > 0)
        //    interfaces += ", " + model.Interfaces.Select(x => x.Name).ToJoinedString(", ");

        yield return $"{namespaceTab}public abstract partial class {table.Model.CsType.Name}(RowData rowData, DataSourceAccess dataSource) : Immutable<{table.Model.CsType.Name}, {model.Database.CsType.Name}>(rowData, dataSource), {interfaces}";
        //yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} {table.Model.CsTypeName} : {interfaces}";

        yield return namespaceTab + "{";

        foreach (var row in valueProps.Where(x => x.EnumProperty != null && x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(options, x)))
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

            foreach (var defaultVal in c.DefaultValues)
                yield return $"{namespaceTab}{tab}[Default(DatabaseType.{defaultVal.DatabaseType}, \"{defaultVal.Value}\")]";

            if (valueProperty.EnumProperty != null)
                yield return $"{namespaceTab}{tab}[Enum({string.Join(", ", valueProperty.EnumProperty.Value.EnumValues.Select(x => $"\"{x.name}\""))})]";

            yield return $"{namespaceTab}{tab}[Column(\"{c.DbName}\")]";
            yield return $"{namespaceTab}{tab}public abstract {c.ValueProperty.CsType.Name}{GetPropertyNullable(c)} {c.ValueProperty.PropertyName} {{ get; }}";
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
                yield return $"{namespaceTab}{tab}public abstract {otherPart.ColumnIndex.Table.Model.CsType.Name}{(options.UseNullableReferenceTypes ? "?" : "")} {relationProperty.PropertyName} {{ get; }}";
            else
                yield return $"{namespaceTab}{tab}public abstract IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}> {relationProperty.PropertyName} {{ get; }}";

            yield return $"";
        }

        yield return namespaceTab + "}";
    }

    private string GetPropertyNullable(ColumnDefinition column)
    {
        return (options.UseNullableReferenceTypes || column.ValueProperty.CsNullable) && (column.Nullable || column.AutoIncrement || column.DefaultValues.Length != 0) ? "?" : "";
    }

    private IEnumerable<string> WriteEnum(ModelFileFactoryOptions options, ValueProperty property)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;

        yield return $"{namespaceTab}public enum {property.CsType.Name}";
        yield return namespaceTab + "{";
        //yield return $"{tab}{tab}Empty,";

        foreach (var val in property.EnumProperty!.Value.EnumValues)
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
