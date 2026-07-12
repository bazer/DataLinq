using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Core.Factories.Models;

public class ModelFileFactoryOptions
{
    public string? NamespaceName { get; set; } = null; //"Models";
    public string Tab { get; set; } = "    ";
    //public bool UseCache { get; set; } = true;
    public bool UseFileScopedNamespaces { get; set; }
    public bool UseNullableReferenceTypes { get; set; } = true;
    public bool SeparateTablesAndViews { get; set; } = false;
    public GeneratedFileStamp? GeneratedFileStamp { get; set; }
    public DataLinqModelLayoutConfig ModelLayout { get; set; } = DataLinqModelLayoutConfig.Default;
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

        var databaseNamespaceName = options.NamespaceName ?? database.CsType.Namespace;
        var databaseUsings = options.Usings
            .Where(name => name != databaseNamespaceName)
            .Concat(database.Usings?.Select(x => x.FullNamespaceName) ?? new List<string>())
            .Distinct()
            .Where(x => x != null)
            .Select(name => (name.StartsWith("System"), name))
            .OrderByDescending(x => x.Item1)
            .ThenBy(x => x.name)
            .Select(x => x.name);

        yield return ($"{database.CsType.Name}.cs",
                FileHeader(databaseNamespaceName, options.UseFileScopedNamespaces, databaseUsings)
                .Concat(DatabaseFileContents(database, database.CsType.Name, options))
                .Concat(FileFooter(options.UseFileScopedNamespaces))
                .ToJoinedString("\n"));

        foreach (var table in database.TableModels.Where(x => !x.IsStub))
        {
            var namespaceName = options.NamespaceName ?? table.Model.CsType.Namespace;
            if (namespaceName == null)
                throw new Exception($"Namespace is null for '{table.Model.CsType.Name}'");

            var sourceUsings = table.Model.Usings?.Select(x => x.FullNamespaceName) ?? new List<string>();
            var usings = options.Usings
                .Where(name => name != namespaceName)
                .Concat(sourceUsings)
                .Concat(table.Model.RelationProperties.Values
                    .Where(x => x.RelationPart.Type == RelationPartType.CandidateKey)
                    .Select(x => "System.Collections.Generic"))
                .Distinct()
                .Where(x => x != null)
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

        foreach (var indexCache in database.IndexCache)
            yield return $"{namespaceTab}{FormatIndexCacheAttribute(indexCache)}";

        yield return $"{namespaceTab}[Database({FormatStringLiteral(database.Name)})]";
        yield return $"{namespaceTab}public partial class {dbName}(IDataLinqReadSource readSource) : IDatabaseModel<{dbName}>";
        //yield return $"{namespaceTab}public interface {dbName} : IDatabaseModel";
        yield return namespaceTab + "{";

        foreach (var t in database.TableModels.OrderBy(x => x.Table.DbName))
        {
            yield return $"{namespaceTab}{tab}public DbRead<{t.Model.CsType.Name}> {t.CsPropertyName} {{ get; }} = new(readSource);";
            //yield return $"{namespaceTab}{tab}DbRead<I{t.Model.CsTypeName}> {t.CsPropertyName} {{ get; }}";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ModelFileContents(ModelDefinition model, ModelFileFactoryOptions options)
    {
        var valueProps = OrderValueProperties(model, options.ModelLayout);
        var relationProps = OrderRelationProperties(model);

        foreach (var row in valueProps.Where(x => x.EnumProperty != null && x.EnumProperty.Value.DeclaredInModelFile && !x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(options, x)))
            yield return row;

        if (model.ModelInstanceInterface != null)
            foreach (var row in WriteInterface(model.ModelInstanceInterface.Value))
                yield return row;

        foreach (var row in WriteClass(model, options, valueProps, relationProps))
            yield return row;
    }

    private static List<ValueProperty> OrderValueProperties(
        ModelDefinition model,
        DataLinqModelLayoutConfig layout)
    {
        IEnumerable<ValueProperty> ordered = layout.PropertyOrder switch
        {
            DataLinqModelPropertyOrder.Alphabetical => model.ValueProperties.Values
                .OrderBy(static property => property.PropertyName, StringComparer.Ordinal),
            _ => model.ValueProperties.Values
                .OrderBy(static property => property.Column.Index)
                .ThenBy(static property => property.Type)
                .ThenBy(static property => property.PropertyName, StringComparer.Ordinal)
        };

        if (layout.PrimaryKeyPlacement == DataLinqModelPrimaryKeyPlacement.Top ||
            layout.ForeignKeyPlacement == DataLinqModelForeignKeyPlacement.Top)
        {
            ordered = ordered
                .Select(static (property, index) => new { property, index })
                .OrderBy(item => GetValuePropertyPlacementRank(item.property, layout))
                .ThenBy(static item => item.index)
                .Select(static item => item.property);
        }

        return ordered.ToList();
    }

    private static int GetValuePropertyPlacementRank(
        ValueProperty property,
        DataLinqModelLayoutConfig layout)
    {
        if (layout.PrimaryKeyPlacement == DataLinqModelPrimaryKeyPlacement.Top &&
            property.Column.PrimaryKey)
        {
            return 0;
        }

        if (layout.ForeignKeyPlacement == DataLinqModelForeignKeyPlacement.Top &&
            IsForeignKeyValueProperty(property))
        {
            return layout.PrimaryKeyPlacement == DataLinqModelPrimaryKeyPlacement.Top ? 1 : 0;
        }

        return 2;
    }

    private static bool IsForeignKeyValueProperty(ValueProperty property) =>
        property.Column.ForeignKey ||
        property.Column.ColumnIndices.Any(static index =>
            index.RelationParts.Any(static relationPart => relationPart.Type == RelationPartType.ForeignKey));

    private static List<RelationProperty> OrderRelationProperties(ModelDefinition model) =>
        model.RelationProperties.Values
            .OrderBy(static property => property.Type)
            .ThenBy(static property => property.PropertyName, StringComparer.Ordinal)
            .ToList();

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

        foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(model.Attributes), namespaceTab))
            yield return row;

        foreach (var comment in model.Attributes.OfType<CommentAttribute>())
            yield return $"{namespaceTab}{FormatCommentAttribute(comment)}";

        foreach (var check in model.Attributes.OfType<CheckAttribute>())
            yield return $"{namespaceTab}{FormatCheckAttribute(check)}";

        if (table.explicitUseCache.HasValue)
            yield return table.explicitUseCache.Value
                ? $"{namespaceTab}[UseCache]"
                : $"{namespaceTab}[UseCache(false)]";

        foreach (var limit in table.CacheLimits)
            yield return $"{namespaceTab}[CacheLimit(CacheLimitType.{limit.limitType}, {limit.amount})]";

        foreach (var indexCache in table.IndexCache)
            yield return $"{namespaceTab}{FormatIndexCacheAttribute(indexCache)}";

        if (table is ViewDefinition view)
        {
            yield return $"{namespaceTab}[Definition({FormatStringLiteral(view.Definition ?? string.Empty)})]";
            yield return $"{namespaceTab}[View({FormatStringLiteral(table.DbName)})]";
        }
        else
        {
            yield return $"{namespaceTab}[Table({FormatStringLiteral(table.DbName)})]";
        }

        foreach (var index in table.ColumnIndices
            .Where(x => x.Characteristic != IndexCharacteristic.PrimaryKey &&
                        x.Characteristic != IndexCharacteristic.ForeignKey &&
                        x.Characteristic != IndexCharacteristic.VirtualDataLinq &&
                        x.Columns.Count > 1)
            .OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var columns = index.Columns.Select(x => FormatStringLiteral(x.DbName)).ToJoinedString(", ");
            yield return $"{namespaceTab}[Index({FormatStringLiteral(index.Name)}, IndexCharacteristic.{index.Characteristic}, IndexType.{index.Type}, {columns})]";
        }

        if (model.ModelInstanceInterface != null)
            yield return $"{namespaceTab}[Interface<{model.ModelInstanceInterface.Value.Name}>]";

        var interfaces = table.Type == TableType.Table ? "ITableModel" : "IViewModel";

        interfaces += $"<{model.Database.CsType.Name}>";
        //interfaces += $", I{table.Model.CsType.Name}";
        //if (model.Interfaces?.Length > 0)
        //    interfaces += ", " + model.Interfaces.Select(x => x.Name).ToJoinedString(", ");

        yield return $"{namespaceTab}public abstract partial class {table.Model.CsType.Name}(IRowData rowData, IDataLinqReadSource readSource) : Immutable<{table.Model.CsType.Name}, {model.Database.CsType.Name}>(rowData, readSource), {interfaces}";

        yield return namespaceTab + "{";

        foreach (var row in valueProps.Where(x => x.EnumProperty != null && x.EnumProperty.Value.DeclaredInModelFile && x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(options, x)))
            yield return tab + row;

        var unwrittenRelations = new HashSet<RelationProperty>(relationProps);
        if (options.ModelLayout.RelationPlacement == DataLinqModelRelationPlacement.Top)
        {
            foreach (var relationProperty in relationProps)
            {
                foreach (var row in WriteRelationProperty(relationProperty, options))
                    yield return row;

                unwrittenRelations.Remove(relationProperty);
            }
        }

        foreach (var valueProperty in valueProps)
        {
            foreach (var row in WriteValueProperty(valueProperty))
                yield return row;

            if (options.ModelLayout.RelationPlacement != DataLinqModelRelationPlacement.WithForeignKey)
                continue;

            foreach (var relationProperty in GetRelationsAfterValueProperty(valueProperty, valueProps, relationProps))
            {
                if (!unwrittenRelations.Remove(relationProperty))
                    continue;

                foreach (var row in WriteRelationProperty(relationProperty, options))
                    yield return row;
            }
        }

        foreach (var relationProperty in relationProps)
        {
            if (!unwrittenRelations.Remove(relationProperty))
                continue;

            foreach (var row in WriteRelationProperty(relationProperty, options))
                yield return row;
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> WriteValueProperty(ValueProperty valueProperty)
    {
        var c = valueProperty.Column;

        foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(valueProperty.Attributes), $"{namespaceTab}{tab}"))
            yield return row;

        foreach (var comment in valueProperty.Attributes.OfType<CommentAttribute>())
            yield return $"{namespaceTab}{tab}{FormatCommentAttribute(comment)}";

        if (c.PrimaryKey)
            yield return $"{namespaceTab}{tab}[PrimaryKey]";

        foreach (var index in c.ColumnIndices.Where(x => x.Characteristic != IndexCharacteristic.PrimaryKey && x.Characteristic != IndexCharacteristic.ForeignKey && x.Characteristic != IndexCharacteristic.VirtualDataLinq && x.Columns.Count == 1))
        {
            yield return $"{namespaceTab}{tab}[Index({FormatStringLiteral(index.Name)}, IndexCharacteristic.{index.Characteristic}, IndexType.{index.Type})]";
        }

        foreach (var index in c.ColumnIndices)
        {
            foreach (var relationPart in index.RelationParts.Where(x => x.Type == RelationPartType.ForeignKey))
            {
                var columnOrdinal = relationPart.ColumnIndex.Columns.IndexOf(c);
                var candidateColumn = relationPart.Relation.CandidateKey.ColumnIndex.Columns[columnOrdinal];
                var foreignKeyArguments = new List<string>
                {
                    SymbolDisplay.FormatLiteral(relationPart.Relation.CandidateKey.ColumnIndex.Table.DbName, quote: true),
                    SymbolDisplay.FormatLiteral(candidateColumn.DbName, quote: true),
                    SymbolDisplay.FormatLiteral(relationPart.Relation.ConstraintName, quote: true)
                };

                if (relationPart.ColumnIndex.Columns.Count > 1)
                    foreignKeyArguments.Add((columnOrdinal + 1).ToString());

                if (relationPart.Relation.OnUpdate != ReferentialAction.Unspecified ||
                    relationPart.Relation.OnDelete != ReferentialAction.Unspecified)
                {
                    foreignKeyArguments.Add($"ReferentialAction.{relationPart.Relation.OnUpdate}");
                    foreignKeyArguments.Add($"ReferentialAction.{relationPart.Relation.OnDelete}");
                }

                yield return $"{namespaceTab}{tab}[ForeignKey({foreignKeyArguments.ToJoinedString(", ")})]";
            }
        }

        if (c.AutoIncrement)
            yield return $"{namespaceTab}{tab}[AutoIncrement]";

        if (c.Nullable)
            yield return $"{namespaceTab}{tab}[Nullable]";

        foreach (var dbType in c.DbTypes.OrderBy(x => x.DatabaseType))
        {
            if (dbType.Signed.HasValue && dbType.Decimals.HasValue && dbType.Length.HasValue)
                yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, {FormatStringLiteral(dbType.Name)}, {dbType.Length}, {dbType.Decimals}, {(dbType.Signed.Value ? "true" : "false")})]";
            else if (dbType.Signed.HasValue && dbType.Length.HasValue)
                yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, {FormatStringLiteral(dbType.Name)}, {dbType.Length}, {(dbType.Signed.Value ? "true" : "false")})]";
            else if (dbType.Signed.HasValue && !dbType.Length.HasValue)
                yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, {FormatStringLiteral(dbType.Name)}, {(dbType.Signed.Value ? "true" : "false")})]";
            else if (dbType.Length.HasValue && dbType.Decimals.HasValue)
                yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, {FormatStringLiteral(dbType.Name)}, {dbType.Length}, {dbType.Decimals})]";
            else if (dbType.Length.HasValue)
                yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, {FormatStringLiteral(dbType.Name)}, {dbType.Length})]";
            else
                yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, {FormatStringLiteral(dbType.Name)})]";
        }

        foreach (var guidStorage in valueProperty.Attributes
            .OfType<GuidStorageAttribute>()
            .OrderBy(x => x.DatabaseType))
        {
            yield return guidStorage.DatabaseType == DatabaseType.Default
                ? $"{namespaceTab}{tab}[GuidStorage(GuidStorageFormat.{guidStorage.Format})]"
                : $"{namespaceTab}{tab}[GuidStorage(DatabaseType.{guidStorage.DatabaseType}, GuidStorageFormat.{guidStorage.Format})]";
        }

        if (valueProperty.HasDefaultValue())
        {
            var defaultAttr = valueProperty.GetDefaultAttribute();
            if (defaultAttr is DefaultCurrentTimestampAttribute)
                yield return $"{namespaceTab}{tab}[DefaultCurrentTimestamp]";
            else if (defaultAttr is DefaultNewUUIDAttribute)
                yield return $"{namespaceTab}{tab}[DefaultNewUUID]";
            else if (defaultAttr is DefaultSqlAttribute defaultSql)
                yield return $"{namespaceTab}{tab}[DefaultSql(DatabaseType.{defaultSql.DatabaseType}, {SymbolDisplay.FormatLiteral(defaultSql.Expression, quote: true)})]";
            else if (defaultAttr != null)
                yield return $"{namespaceTab}{tab}[Default({valueProperty.GetDefaultValueCode()})]";
        }

        if (valueProperty.EnumProperty != null && HasDatabaseEnumType(valueProperty))
            yield return $"{namespaceTab}{tab}[Enum({string.Join(", ", valueProperty.EnumProperty.Value.CsValuesOrDbValues.Select(x => FormatStringLiteral(x.name)))})]";

        yield return $"{namespaceTab}{tab}[Column({FormatStringLiteral(c.DbName)})]";
        yield return $"{namespaceTab}{tab}public abstract {c.ValueProperty.CsType.Name}{GetPropertyNullable(c)} {c.ValueProperty.PropertyName} {{ get; }}";
        yield return "";
    }

    private IEnumerable<string> WriteRelationProperty(
        RelationProperty relationProperty,
        ModelFileFactoryOptions options)
    {
        var otherPart = relationProperty.RelationPart.GetOtherSide();

        List<string> relationParameters = [FormatStringLiteral(otherPart.ColumnIndex.Table.DbName)];

        if (otherPart.ColumnIndex.Columns.Count == 1)
            relationParameters.Add(FormatStringLiteral(otherPart.ColumnIndex.Columns[0].DbName));
        else
            relationParameters.Add($"new string[] {{ {otherPart.ColumnIndex.Columns.Select(x => FormatStringLiteral(x.DbName)).ToJoinedString(", ")} }}");

        if (relationProperty.RelationName != null)
            relationParameters.Add(FormatStringLiteral(relationProperty.RelationName));

        yield return $"{namespaceTab}{tab}[Relation({relationParameters.ToJoinedString(", ")})]";

        if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
        {
            var nullableAnnotation = options.UseNullableReferenceTypes && relationProperty.CsNullable ? "?" : "";
            yield return $"{namespaceTab}{tab}public abstract {otherPart.ColumnIndex.Table.Model.CsType.Name}{nullableAnnotation} {relationProperty.PropertyName} {{ get; }}";
        }
        else
        {
            yield return $"{namespaceTab}{tab}public abstract IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}> {relationProperty.PropertyName} {{ get; }}";
        }

        yield return "";
    }

    private static IEnumerable<RelationProperty> GetRelationsAfterValueProperty(
        ValueProperty valueProperty,
        IReadOnlyList<ValueProperty> valueProperties,
        IEnumerable<RelationProperty> relationProperties)
    {
        foreach (var relationProperty in relationProperties)
        {
            var relationPart = relationProperty.RelationPart;
            if (relationPart.Type != RelationPartType.ForeignKey)
                continue;

            var foreignKeyColumns = relationPart.ColumnIndex.Columns;
            if (!foreignKeyColumns.Contains(valueProperty.Column))
                continue;

            var lastForeignKeyValueProperty = valueProperties
                .Where(property => foreignKeyColumns.Contains(property.Column))
                .LastOrDefault();

            if (ReferenceEquals(lastForeignKeyValueProperty?.Column, valueProperty.Column))
                yield return relationProperty;
        }
    }

    private static string FormatCommentAttribute(CommentAttribute comment)
    {
        var text = SymbolDisplay.FormatLiteral(comment.Text, quote: true);

        return comment.DatabaseType == DatabaseType.Default
            ? $"[Comment({text})]"
            : $"[Comment(DatabaseType.{comment.DatabaseType}, {text})]";
    }

    private static string FormatCheckAttribute(CheckAttribute check)
    {
        var name = SymbolDisplay.FormatLiteral(check.Name, quote: true);
        var expression = SymbolDisplay.FormatLiteral(check.Expression, quote: true);

        return check.DatabaseType == DatabaseType.Default
            ? $"[Check({name}, {expression})]"
            : $"[Check(DatabaseType.{check.DatabaseType}, {name}, {expression})]";
    }

    private static string FormatIndexCacheAttribute((IndexCacheType indexCacheType, int? amount) indexCache) =>
        indexCache.amount.HasValue
            ? $"[IndexCache(IndexCacheType.{indexCache.indexCacheType}, {indexCache.amount.Value})]"
            : $"[IndexCache(IndexCacheType.{indexCache.indexCacheType})]";

    private static string FormatStringLiteral(string value) =>
        SymbolDisplay.FormatLiteral(value, quote: true);

    private static bool HasDatabaseEnumType(ValueProperty property) =>
        property.Column.DbTypes.Any(x => string.Equals(x.Name, "enum", StringComparison.OrdinalIgnoreCase));

    private static string? GetDocumentationComment(IEnumerable<Attribute> attributes)
    {
        var comments = attributes.OfType<CommentAttribute>().ToList();

        return comments.FirstOrDefault(x => x.DatabaseType == DatabaseType.Default)?.Text
            ?? comments.FirstOrDefault()?.Text;
    }

    private static IEnumerable<string> FormatSummaryXmlDocs(string? text, string indent)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        yield return $"{indent}/// <summary>";
        foreach (var line in text!.Replace("\r\n", "\n").Split('\n'))
            yield return $"{indent}/// {EscapeXmlDoc(line)}";
        yield return $"{indent}/// </summary>";
    }

    private static string EscapeXmlDoc(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

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
        foreach (var row in GeneratedFilePreamble.Create(new GeneratedFilePreambleOptions
        {
            UseNullableReferenceTypes = options.UseNullableReferenceTypes,
            Stamp = options.GeneratedFileStamp,
            Kind = GeneratedFilePreambleKind.ModelDeclaration
        }))
            yield return row;

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
