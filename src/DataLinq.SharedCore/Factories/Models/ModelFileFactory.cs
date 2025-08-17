using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private string namespaceTab;
    private string tab;

    private readonly ModelFileFactoryOptions options;

    public ModelFileFactory(ModelFileFactoryOptions options)
    {
        this.options = options;

        namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        tab = options.Tab;
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

        if (model.ModelInstanceInterface != null)
            foreach (var row in WriteInterface(model.ModelInstanceInterface.Value))
                yield return row;

        foreach (var row in WriteClass(model, options, valueProps, relationProps))
            yield return row;
    }

    private IEnumerable<string> WriteInterface(CsTypeDeclaration modelInterface)
    {
        yield return $"{namespaceTab}public partial interface {modelInterface.Name}";
        yield return namespaceTab + "{";
        yield return namespaceTab + "}";
        yield return "";
    }

    private IEnumerable<string> WriteClass(ModelDefinition model, ModelFileFactoryOptions options, List<ValueProperty> valueProps, List<RelationProperty> relationProps)
    {
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

        if (model.ModelInstanceInterface != null)
            yield return $"{namespaceTab}[Interface<{model.ModelInstanceInterface.Value.Name}>]";

        var interfaces = table.Type == TableType.Table ? "ITableModel" : "IViewModel";

        interfaces += $"<{model.Database.CsType.Name}>";
        //interfaces += $", I{table.Model.CsType.Name}";
        //if (model.Interfaces?.Length > 0)
        //    interfaces += ", " + model.Interfaces.Select(x => x.Name).ToJoinedString(", ");

        yield return $"{namespaceTab}public abstract partial class {table.Model.CsType.Name}(IRowData rowData, IDataSourceAccess dataSource) : Immutable<{table.Model.CsType.Name}, {model.Database.CsType.Name}>(rowData, dataSource), {interfaces}";
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

            if (valueProperty.HasDefaultValue())
            {
                var defaultAttr = valueProperty.GetDefaultAttribute();
                if (defaultAttr is DefaultCurrentTimestampAttribute)
                    yield return $"{namespaceTab}{tab}[DefaultCurrentTimestamp]";
                else if (defaultAttr is DefaultNewUUIDAttribute)
                    yield return $"{namespaceTab}{tab}[DefaultNewUUID]";
                else if (defaultAttr != null)
                    yield return $"{namespaceTab}{tab}[Default({FormatDefaultValue(defaultAttr.Value)})]";
            }

            if (valueProperty.EnumProperty != null)
                yield return $"{namespaceTab}{tab}[Enum({string.Join(", ", valueProperty.EnumProperty.Value.CsValuesOrDbValues.Select(x => $"\"{x.name}\""))})]";

            yield return $"{namespaceTab}{tab}[Column(\"{c.DbName}\")]";
            yield return $"{namespaceTab}{tab}public abstract {c.ValueProperty.CsType.Name}{GetPropertyNullable(c)} {c.ValueProperty.PropertyName} {{ get; }}";
            yield return $"";
        }

        foreach (var relationProperty in relationProps)
        {
            var otherPart = relationProperty.RelationPart.GetOtherSide();

            List<string> relationParameters = [$"\"{otherPart.ColumnIndex.Table.DbName}\""];

            if (otherPart.ColumnIndex.Columns.Count == 1)
                relationParameters.Add($"\"{otherPart.ColumnIndex.Columns[0].DbName}\"");
            else
                relationParameters.Add($"\"[{otherPart.ColumnIndex.Columns.Select(x => $"\"{x.DbName}\"").ToJoinedString(", ")}]");

            if (relationProperty.RelationName != null)
                relationParameters.Add($"\"{relationProperty.RelationName}\"");

            yield return $"{namespaceTab}{tab}[Relation({relationParameters.ToJoinedString(", ")})]";


            if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
                yield return $"{namespaceTab}{tab}public abstract {otherPart.ColumnIndex.Table.Model.CsType.Name}{(options.UseNullableReferenceTypes ? "?" : "")} {relationProperty.PropertyName} {{ get; }}";
            else
                yield return $"{namespaceTab}{tab}public abstract IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}> {relationProperty.PropertyName} {{ get; }}";

            yield return $"";
        }

        yield return namespaceTab + "}";
    }

    private string FormatDefaultValue(object value)
    {
        if (value is string str)
            return $"\"{str}\"";

        if (value is bool b)
            return b ? "true" : "false";

        if (value is DateTime dt)
            return $"DateTime.Parse(\"{dt:yyyy-MM-dd HH:mm:ss}\")";

        if (value is DateTimeOffset dto)
            return $"DateTimeOffset.Parse(\"{dto:yyyy-MM-dd HH:mm:ss}\")";

        if (value is TimeSpan ts)
            return $"TimeSpan.Parse(\"{ts:hh\\:mm\\:ss}\")";

        return value.ToString();
    }

    private string GetPropertyNullable(ColumnDefinition column)
    {
        // The C# property should be nullable if:
        // 1. The database column itself is nullable.
        // OR
        // 2. The column is an auto-incrementing key.
        // OR
        // 3. The column has a default value (either from the DB or a [Default] attribute).
        bool needsCSharpNullable =
            column.Nullable ||
            column.AutoIncrement;

        if (!needsCSharpNullable)
            return ""; // The property is definitely not nullable.

        // At this point, we know the property MUST be nullable in C#.
        // Now, we determine if we need to add a '?' to the type name.

        // Identify the few known C# reference types that are mapped to columns.
        string csTypeName = column.ValueProperty.CsType.Name;
        bool isReferenceType = (csTypeName == "string" || csTypeName == "byte[]");

        if (isReferenceType)
        {
            // For reference types, only add '?' if Nullable Reference Types are enabled in the config.
            return options.UseNullableReferenceTypes ? "?" : "";
        }
        else
        {
            // For ALL other types (int, bool, DateTime, Guid, and user-defined enums/structs),
            // treat them as value types. If they need to be nullable, they MUST have a '?'.
            return "?";
        }
    }

    private IEnumerable<string> WriteEnum(ModelFileFactoryOptions options, ValueProperty property)
    {
        yield return $"{namespaceTab}public enum {property.CsType.Name}";
        yield return namespaceTab + "{";
        //yield return $"{tab}{tab}Empty,";

        foreach (var val in property.EnumProperty!.Value.CsValuesOrDbValues)
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
