using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Extensions.Helpers;
using Microsoft.CodeAnalysis;

namespace DataLinq.Metadata;

public class GeneratorFileFactoryOptions
{
    public string? NamespaceName { get; set; } = null;
    public string Tab { get; set; } = "    ";
    public bool UseRecords { get; set; } = false;
    public bool UseFileScopedNamespaces { get; set; } = false;
    public bool UseNullableReferenceTypes { get; set; } = true;
    public bool SeparateTablesAndViews { get; set; } = false;
    public IReadOnlyCollection<ValueProperty> SuppressedDefaultValueProperties { get; set; } = [];
    public List<string> Usings { get; set; } = new List<string> { "System", "System.Diagnostics.CodeAnalysis", "DataLinq", "DataLinq.Interfaces", "DataLinq.Instances", "DataLinq.Attributes", "DataLinq.Mutation" };
}

public class GeneratorFileFactory
{
    private string namespaceTab;
    private string tab;

    public GeneratorFileFactoryOptions Options { get; }

    // Parameterless methods in the Mutable base class that we need to declare as 'new' in derived classes to avoid hiding base methods.
    private static readonly HashSet<string> MutableBaseMethodNames = new(StringComparer.Ordinal)
    {
        "IsNew",
        "IsDeleted",
        "HasChanges",
        "Metadata",
        "GetImmutableInstance",
        "GetRowData",
        "PrimaryKeys",
        "HasPrimaryKeysSet",
        "Reset",
        "SetDeleted",
        "ClearLazy"
    };

    public GeneratorFileFactory(GeneratorFileFactoryOptions options)
    {
        this.Options = options;
        this.Options.UseRecords = false;

        namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        tab = options.Tab;
    }

    public IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseDefinition database)
    {
        foreach (var file in CreateTableModelFiles(database))
            yield return file;

        if (database.TableModels.Any(static x => !x.IsStub))
            yield return CreateDatabaseMetadataBootstrapFile(database);
    }

    internal IEnumerable<(string path, string contents)> CreateTableModelFiles(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Where(static x => !x.IsStub))
            yield return CreateModelFile(table);
    }

    internal (string path, string contents) CreateDatabaseMetadataBootstrapFile(DatabaseDefinition database)
    {
        var namespaceName = Options.NamespaceName ?? database.CsType.Namespace;
        if (string.IsNullOrWhiteSpace(namespaceName))
            throw new Exception($"Namespace is missing for '{database.CsType.Name}'");

        var usings = Options.Usings
            .Where(name => name != namespaceName)
            .Concat(database.Usings?.Select(x => x.FullNamespaceName) ?? new List<string>())
            .Distinct()
            .Where(x => x != null)
            .Select(name => (name.StartsWith("System"), name))
            .OrderByDescending(x => x.Item1)
            .ThenBy(x => x.name)
            .Select(x => x.name);

        var file =
            FileHeader(namespaceName, Options.UseFileScopedNamespaces, usings)
            .Concat(DatabaseMetadataBootstrapFileContents(database))
            .Concat(FileFooter(Options.UseFileScopedNamespaces))
            .ToJoinedString("\n");

        return ($"{database.CsType.Name}.DataLinqMetadata.cs", file);
    }

    internal (string path, string contents) CreateModelFile(TableModel table)
    {
        try
        {
            var namespaceName = Options.NamespaceName ?? table.Model.CsType.Namespace;
            if (string.IsNullOrWhiteSpace(namespaceName))
                throw new Exception($"Namespace is missing for '{table.Model.CsType.Name}'");

            var sourceUsings = table.Model.Usings?.Select(x => x.FullNamespaceName) ?? new List<string>();
            var usings = Options.Usings
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
                FileHeader(namespaceName, Options.UseFileScopedNamespaces, usings)
                .Concat(ModelFileContents(table.Model, Options))
                .Concat(FileFooter(Options.UseFileScopedNamespaces))
                .ToJoinedString("\n");

            var path = GetFilePath(table);

            return (path, file);
        }
        catch (ModelFileGenerationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ModelFileGenerationException(table.Model, exception);
        }
    }

    private IEnumerable<string> ModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options)
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

        if (model.ModelInstanceInterface != null)
            foreach (var row in WriteInterface(model, model.ModelInstanceInterface.Value, options, valueProps))
                yield return row;

        foreach (var row in WriteBaseClassPartial(model, options))
            yield return row;

        foreach (var row in ImmutableModelFileContents(model, Options, valueProps, relationProps))
            yield return row;

        if (model.Table.Type == TableType.Table)
        {
            foreach (var row in MutableModelFileContents(model, Options, valueProps, relationProps))
                yield return row;

            foreach (var row in ExtensionMethodsFileContents(model, Options))
                yield return row;
        }
    }

    private IEnumerable<string> DatabaseMetadataBootstrapFileContents(DatabaseDefinition database)
    {
        var tableModels = database.TableModels
            .Where(static x => !x.IsStub)
            .OrderBy(static x => x.CsPropertyName, StringComparer.Ordinal)
            .ToArray();

        yield return $"{namespaceTab}public partial class {database.CsType.Name} : global::DataLinq.Interfaces.IDatabaseModel<{database.CsType.Name}>";
        yield return namespaceTab + "{";
        yield return $"{namespaceTab}{tab}public static {database.CsType.Name} NewDataLinqDatabase(global::DataLinq.Interfaces.IDataSourceAccess dataSource) =>";
        yield return $"{namespaceTab}{tab}{tab}new {GetGlobalTypeName(database.CsType)}((global::DataLinq.Mutation.DataSourceAccess)dataSource);";
        yield return "";
        yield return $"{namespaceTab}{tab}public static global::DataLinq.Metadata.GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>";
        yield return $"{namespaceTab}{tab}{tab}new(";
        yield return $"{namespaceTab}{tab}[";

        foreach (var tableModel in tableModels)
        {
            var modelType = GetGlobalTypeName(tableModel.Model.CsType);
            var immutableType = GetGlobalImmutableTypeName(tableModel.Model.CsType);
            var mutableType = tableModel.Table.Type == TableType.Table
                ? $"typeof({GetGlobalMutableTypeName(tableModel.Model.CsType)})"
                : "null";

            yield return $"{namespaceTab}{tab}{tab}new(\"{tableModel.CsPropertyName}\", typeof({modelType}), typeof({immutableType}), {mutableType}, new global::System.Func<global::DataLinq.Instances.IRowData, global::DataLinq.Interfaces.IDataSourceAccess, global::DataLinq.Instances.IImmutableInstance>({immutableType}.NewDataLinqImmutableInstance), global::DataLinq.Metadata.TableType.{tableModel.Table.Type}),";
        }

        yield return $"{namespaceTab}{tab}]);";
        yield return "";
        yield return $"{namespaceTab}{tab}public static void SetDataLinqGeneratedMetadata(global::DataLinq.Metadata.DatabaseDefinition metadata)";
        yield return $"{namespaceTab}{tab}" + "{";
        yield return $"{namespaceTab}{tab}{tab}if (metadata is null)";
        yield return $"{namespaceTab}{tab}{tab}{tab}throw new global::System.ArgumentNullException(nameof(metadata));";
        yield return "";
        yield return $"{namespaceTab}{tab}{tab}var tableModels = metadata.TableModels;";
        yield return $"{namespaceTab}{tab}{tab}if (tableModels.Length != {tableModels.Length.ToString(CultureInfo.InvariantCulture)})";
        yield return $"{namespaceTab}{tab}{tab}{tab}throw new global::System.InvalidOperationException(\"Generated DataLinq metadata table model count mismatch. Regenerate the DataLinq model sources with the current generator package.\");";

        for (var i = 0; i < tableModels.Length; i++)
        {
            var modelType = GetGlobalTypeName(tableModels[i].Model.CsType);
            yield return $"{namespaceTab}{tab}{tab}if (tableModels[{i.ToString(CultureInfo.InvariantCulture)}].Model.CsType.Type != typeof({modelType}))";
            yield return $"{namespaceTab}{tab}{tab}{tab}throw new global::System.InvalidOperationException(\"Generated DataLinq metadata table model order mismatch. Regenerate the DataLinq model sources with the current generator package.\");";
            yield return $"{namespaceTab}{tab}{tab}{modelType}.SetDataLinqGeneratedModel(tableModels[{i.ToString(CultureInfo.InvariantCulture)}].Model);";
        }

        yield return $"{namespaceTab}{tab}" + "}";
        yield return "";
        foreach (var row in GeneratedMetadataDraftMethod(database, tableModels))
            yield return row;

        yield return namespaceTab + "}";
        yield return "";
    }

    private IEnumerable<string> GeneratedMetadataDraftMethod(DatabaseDefinition database, IReadOnlyList<TableModel> tableModels)
    {
        var indent1 = $"{namespaceTab}{tab}";
        var indent2 = $"{namespaceTab}{tab}{tab}";
        var indent3 = $"{namespaceTab}{tab}{tab}{tab}";

        yield return $"{indent1}public static global::DataLinq.Core.Factories.MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>";
        yield return $"{indent2}new(";
        yield return $"{indent3}{FormatStringLiteral(database.Name)},";
        yield return $"{indent3}{FormatRuntimeCsTypeDeclaration(database.CsType)})";
        yield return $"{indent2}{{";
        yield return $"{indent3}DbName = {FormatStringLiteral(database.DbName)},";
        foreach (var row in AttributeCollection("Attributes", database.Attributes, indent3))
            yield return row;
        yield return $"{indent3}UseCache = {FormatBool(database.UseCache)},";
        foreach (var row in CacheLimitCollection("CacheLimits", database.CacheLimits, indent3))
            yield return row;
        foreach (var row in CacheCleanupCollection("CacheCleanup", database.CacheCleanup, indent3))
            yield return row;
        foreach (var row in IndexCacheCollection("IndexCache", database.IndexCache, indent3))
            yield return row;
        yield return $"{indent3}TableModels =";
        yield return $"{indent3}[";

        foreach (var tableModel in tableModels)
        {
            foreach (var row in TableModelDraft(tableModel, $"{indent3}{tab}"))
                yield return row;
        }

        yield return $"{indent3}],";
        yield return $"{indent2}}};";
    }

    private IEnumerable<string> TableModelDraft(TableModel tableModel, string indent)
    {
        var childIndent = $"{indent}{tab}";
        var model = tableModel.Model;
        var table = tableModel.Table;

        yield return $"{indent}new global::DataLinq.Core.Factories.MetadataTableModelDraft(";
        yield return $"{childIndent}{FormatStringLiteral(tableModel.CsPropertyName)},";

        foreach (var row in ModelDraft(model, childIndent))
            yield return row;
        yield return $"{childIndent},";

        foreach (var row in TableDraft(model, table, childIndent))
            yield return row;
        yield return $"{indent}),";
    }

    private IEnumerable<string> ModelDraft(ModelDefinition model, string indent)
    {
        var childIndent = $"{indent}{tab}";
        var immutableType = GetGlobalImmutableTypeName(model.CsType);
        var mutableType = model.Table.Type == TableType.Table
            ? GetGlobalMutableTypeName(model.CsType)
            : null;

        yield return $"{indent}new global::DataLinq.Core.Factories.MetadataModelDraft({FormatRuntimeCsTypeDeclaration(model.CsType)})";
        yield return $"{indent}{{";
        yield return $"{childIndent}ImmutableType = {FormatRuntimeCsTypeDeclaration(immutableType)},";
        yield return $"{childIndent}ImmutableFactory = new global::System.Func<global::DataLinq.Instances.IRowData, global::DataLinq.Interfaces.IDataSourceAccess, global::DataLinq.Instances.IImmutableInstance>({immutableType}.NewDataLinqImmutableInstance),";
        var providerKeyAccessor = GetProviderKeyRowStoreAccessorExpression(model);
        if (providerKeyAccessor is not null)
            yield return $"{childIndent}ProviderKeyRowStoreAccessor = {providerKeyAccessor},";
        yield return $"{childIndent}MutableType = {FormatNullableRuntimeCsTypeDeclaration(mutableType)},";
        yield return $"{childIndent}ModelInstanceInterface = {FormatNullableCsTypeDeclaration(model.ModelInstanceInterface)},";
        foreach (var row in CsTypeCollection("OriginalInterfaces", model.OriginalInterfaces, childIndent))
            yield return row;
        foreach (var row in AttributeCollection("Attributes", model.Attributes, childIndent))
            yield return row;
        foreach (var row in ValuePropertyCollection(model, childIndent))
            yield return row;
        foreach (var row in RelationPropertyCollection(model, childIndent))
            yield return row;
        yield return $"{indent}}}";
    }

    private IEnumerable<string> TableDraft(ModelDefinition model, TableDefinition table, string indent)
    {
        var childIndent = $"{indent}{tab}";
        var explicitUseCache = model.Attributes
            .OfType<UseCacheAttribute>()
            .LastOrDefault()?
            .UseCache;

        yield return $"{indent}new global::DataLinq.Core.Factories.MetadataTableDraft({FormatStringLiteral(table.DbName)})";
        yield return $"{indent}{{";
        yield return $"{childIndent}Type = global::DataLinq.Metadata.TableType.{table.Type},";
        if (table is ViewDefinition view)
            yield return $"{childIndent}Definition = {FormatNullableStringLiteral(view.Definition)},";
        yield return $"{childIndent}UseCache = {FormatNullableBool(explicitUseCache)},";
        foreach (var row in CacheLimitCollection("CacheLimits", table.CacheLimits, childIndent))
            yield return row;
        foreach (var row in IndexCacheCollection("IndexCache", table.IndexCache, childIndent))
            yield return row;
        yield return $"{indent}}}";
    }

    private IEnumerable<string> ValuePropertyCollection(ModelDefinition model, string indent)
    {
        yield return $"{indent}ValueProperties =";
        yield return $"{indent}[";

        var properties = model.Table.Columns
            .OrderBy(static column => column.Index)
            .ThenBy(static column => column.DbName, StringComparer.Ordinal)
            .Select(static column => column.ValueProperty);

        foreach (var property in properties)
        {
            foreach (var row in ValuePropertyDraft(property, $"{indent}{tab}"))
                yield return row;
        }

        yield return $"{indent}],";
    }

    private IEnumerable<string> ValuePropertyDraft(ValueProperty property, string indent)
    {
        var childIndent = $"{indent}{tab}";

        yield return $"{indent}new global::DataLinq.Core.Factories.MetadataValuePropertyDraft(";
        yield return $"{childIndent}{FormatStringLiteral(property.PropertyName)},";
        yield return $"{childIndent}{FormatValuePropertyCsTypeDeclaration(property)},";
        foreach (var row in ColumnDraft(property.Column, childIndent))
            yield return row;
        yield return $"{indent})";
        yield return $"{indent}{{";
        foreach (var row in AttributeCollection("Attributes", property.Attributes, childIndent))
            yield return row;
        yield return $"{childIndent}CsNullable = {FormatBool(property.CsNullable)},";
        yield return $"{childIndent}CsSize = {FormatNullableInt(property.CsSize)},";
        yield return $"{childIndent}EnumProperty = {FormatNullableEnumProperty(property.EnumProperty)},";
        yield return $"{indent}}},";
    }

    private IEnumerable<string> ColumnDraft(ColumnDefinition column, string indent)
    {
        var childIndent = $"{indent}{tab}";

        yield return $"{indent}new global::DataLinq.Core.Factories.MetadataColumnDraft({FormatStringLiteral(column.DbName)})";
        yield return $"{indent}{{";
        foreach (var row in DatabaseColumnTypeCollection("DbTypes", column.DbTypes, childIndent))
            yield return row;
        if (column.DbTypes.Count > 0)
            yield return $"{childIndent}OwnsDbTypes = true,";
        yield return $"{childIndent}PrimaryKey = {FormatBool(column.PrimaryKey)},";
        yield return $"{childIndent}ForeignKey = {FormatBool(column.ForeignKey)},";
        yield return $"{childIndent}AutoIncrement = {FormatBool(column.AutoIncrement)},";
        yield return $"{childIndent}Nullable = {FormatBool(column.Nullable)},";
        yield return $"{indent}}}";
    }

    private IEnumerable<string> RelationPropertyCollection(ModelDefinition model, string indent)
    {
        yield return $"{indent}RelationProperties =";
        yield return $"{indent}[";

        foreach (var property in model.RelationProperties.Values.OrderBy(static property => property.PropertyName, StringComparer.Ordinal))
        {
            foreach (var row in RelationPropertyDraft(property, $"{indent}{tab}"))
                yield return row;
        }

        yield return $"{indent}],";
    }

    private IEnumerable<string> RelationPropertyDraft(RelationProperty property, string indent)
    {
        var childIndent = $"{indent}{tab}";

        yield return $"{indent}new global::DataLinq.Core.Factories.MetadataRelationPropertyDraft(";
        yield return $"{childIndent}{FormatStringLiteral(property.PropertyName)},";
        yield return $"{childIndent}{FormatCsTypeDeclaration(property.CsType)})";
        yield return $"{indent}{{";
        foreach (var row in AttributeCollection("Attributes", property.Attributes, childIndent))
            yield return row;
        yield return $"{childIndent}CsNullable = {FormatBool(property.CsNullable)},";
        yield return $"{childIndent}RelationName = {FormatNullableStringLiteral(property.RelationName)},";
        yield return $"{indent}}},";
    }

    private IEnumerable<string> AttributeCollection(string propertyName, IEnumerable<Attribute> attributes, string indent)
    {
        yield return $"{indent}{propertyName} =";
        yield return $"{indent}[";

        foreach (var attribute in attributes)
            yield return $"{indent}{tab}{FormatAttribute(attribute)},";

        yield return $"{indent}],";
    }

    private IEnumerable<string> CsTypeCollection(string propertyName, IEnumerable<CsTypeDeclaration> csTypes, string indent)
    {
        yield return $"{indent}{propertyName} =";
        yield return $"{indent}[";

        foreach (var csType in csTypes)
            yield return $"{indent}{tab}{FormatCsTypeDeclaration(csType)},";

        yield return $"{indent}],";
    }

    private IEnumerable<string> DatabaseColumnTypeCollection(string propertyName, IEnumerable<DatabaseColumnType> dbTypes, string indent)
    {
        yield return $"{indent}{propertyName} =";
        yield return $"{indent}[";

        foreach (var dbType in dbTypes)
        {
            yield return $"{indent}{tab}new global::DataLinq.Metadata.DatabaseColumnType(global::DataLinq.DatabaseType.{dbType.DatabaseType}, {FormatStringLiteral(dbType.Name)}, {FormatNullableULong(dbType.Length)}, {FormatNullableUInt(dbType.Decimals)}, {FormatNullableBool(dbType.Signed)}),";
        }

        yield return $"{indent}],";
    }

    private IEnumerable<string> CacheLimitCollection(string propertyName, IEnumerable<(CacheLimitType limitType, long amount)> limits, string indent)
    {
        yield return $"{indent}{propertyName} =";
        yield return $"{indent}[";

        foreach (var limit in limits)
            yield return $"{indent}{tab}(global::DataLinq.Attributes.CacheLimitType.{limit.limitType}, {FormatLong(limit.amount)}),";

        yield return $"{indent}],";
    }

    private IEnumerable<string> CacheCleanupCollection(string propertyName, IEnumerable<(CacheCleanupType cleanupType, long amount)> limits, string indent)
    {
        yield return $"{indent}{propertyName} =";
        yield return $"{indent}[";

        foreach (var limit in limits)
            yield return $"{indent}{tab}(global::DataLinq.Attributes.CacheCleanupType.{limit.cleanupType}, {FormatLong(limit.amount)}),";

        yield return $"{indent}],";
    }

    private IEnumerable<string> IndexCacheCollection(string propertyName, IEnumerable<(IndexCacheType indexCacheType, int? amount)> limits, string indent)
    {
        yield return $"{indent}{propertyName} =";
        yield return $"{indent}[";

        foreach (var limit in limits)
            yield return $"{indent}{tab}(global::DataLinq.Attributes.IndexCacheType.{limit.indexCacheType}, {FormatNullableInt(limit.amount)}),";

        yield return $"{indent}],";
    }

    private static string FormatRuntimeCsTypeDeclaration(CsTypeDeclaration csType) =>
        FormatRuntimeCsTypeDeclaration(csType, GetGlobalTypeName(csType));

    private static string FormatRuntimeCsTypeDeclaration(CsTypeDeclaration csType, string runtimeTypeName) =>
        FormatRuntimeCsTypeDeclaration(runtimeTypeName);

    private static string FormatRuntimeCsTypeDeclaration(string runtimeTypeName) =>
        $"new global::DataLinq.Metadata.CsTypeDeclaration(typeof({runtimeTypeName}))";

    private static string FormatNullableRuntimeCsTypeDeclaration(string? runtimeTypeName) =>
        runtimeTypeName is not null
            ? FormatRuntimeCsTypeDeclaration(runtimeTypeName)
            : "null";

    private static string FormatCsTypeDeclaration(CsTypeDeclaration csType) =>
        $"new global::DataLinq.Metadata.CsTypeDeclaration({FormatStringLiteral(csType.Name)}, {FormatStringLiteral(csType.Namespace)}, global::DataLinq.Metadata.ModelCsType.{csType.ModelCsType})";

    private static string FormatNullableCsTypeDeclaration(CsTypeDeclaration? csType) =>
        csType.HasValue
            ? FormatCsTypeDeclaration(csType.Value)
            : "null";

    private static string FormatValuePropertyCsTypeDeclaration(ValueProperty property)
    {
        var runtimeTypeName = GetValuePropertyRuntimeTypeName(property);
        return runtimeTypeName is null
            ? FormatCsTypeDeclaration(property.CsType)
            : FormatRuntimeCsTypeDeclaration(runtimeTypeName);
    }

    private static string? GetValuePropertyRuntimeTypeName(ValueProperty property)
    {
        if (property.EnumProperty?.DeclaredInClass == true)
            return $"{GetGlobalTypeName(property.Model.CsType)}.{property.CsType.Name}";

        if (property.EnumProperty.HasValue)
            return GetGlobalTypeName(property.CsType);

        if (MetadataTypeConverter.IsKnownCsType(property.CsType.Name))
            return $"global::{MetadataTypeConverter.GetFullTypeName(property.CsType.Name)}";

        return null;
    }

    private static string FormatAttribute(Attribute attribute) => attribute switch
    {
        DatabaseAttribute value => $"new global::DataLinq.Attributes.DatabaseAttribute({FormatStringLiteral(value.Name)})",
        TableAttribute value => $"new global::DataLinq.Attributes.TableAttribute({FormatStringLiteral(value.Name)})",
        ViewAttribute value => $"new global::DataLinq.Attributes.ViewAttribute({FormatStringLiteral(value.Name)})",
        ColumnAttribute value => $"new global::DataLinq.Attributes.ColumnAttribute({FormatStringLiteral(value.Name)})",
        UseCacheAttribute value => $"new global::DataLinq.Attributes.UseCacheAttribute({FormatBool(value.UseCache)})",
        CacheLimitAttribute value => $"new global::DataLinq.Attributes.CacheLimitAttribute(global::DataLinq.Attributes.CacheLimitType.{value.LimitType}, {FormatLong(value.Amount)})",
        CacheCleanupAttribute value => $"new global::DataLinq.Attributes.CacheCleanupAttribute(global::DataLinq.Attributes.CacheCleanupType.{value.LimitType}, {FormatLong(value.Amount)})",
        IndexCacheAttribute value => value.Amount.HasValue
            ? $"new global::DataLinq.Attributes.IndexCacheAttribute(global::DataLinq.Attributes.IndexCacheType.{value.Type}, {value.Amount.Value.ToString(CultureInfo.InvariantCulture)})"
            : $"new global::DataLinq.Attributes.IndexCacheAttribute(global::DataLinq.Attributes.IndexCacheType.{value.Type})",
        DefinitionAttribute value => $"new global::DataLinq.Attributes.DefinitionAttribute({FormatStringLiteral(value.Sql)})",
        CheckAttribute value => value.DatabaseType == DatabaseType.Default
            ? $"new global::DataLinq.Attributes.CheckAttribute({FormatStringLiteral(value.Name)}, {FormatStringLiteral(value.Expression)})"
            : $"new global::DataLinq.Attributes.CheckAttribute(global::DataLinq.DatabaseType.{value.DatabaseType}, {FormatStringLiteral(value.Name)}, {FormatStringLiteral(value.Expression)})",
        CommentAttribute value => value.DatabaseType == DatabaseType.Default
            ? $"new global::DataLinq.Attributes.CommentAttribute({FormatStringLiteral(value.Text)})"
            : $"new global::DataLinq.Attributes.CommentAttribute(global::DataLinq.DatabaseType.{value.DatabaseType}, {FormatStringLiteral(value.Text)})",
        EnumAttribute value => $"new global::DataLinq.Attributes.EnumAttribute({FormatStringArguments(value.Values)})",
        PrimaryKeyAttribute => "new global::DataLinq.Attributes.PrimaryKeyAttribute()",
        NullableAttribute => "new global::DataLinq.Attributes.NullableAttribute()",
        AutoIncrementAttribute => "new global::DataLinq.Attributes.AutoIncrementAttribute()",
        TypeAttribute value => $"new global::DataLinq.Attributes.TypeAttribute(global::DataLinq.DatabaseType.{value.DatabaseType}, {FormatStringLiteral(value.Name)}, {FormatNullableULong(value.Length)}, {FormatNullableUInt(value.Decimals)}, {FormatNullableBool(value.Signed)})",
        ForeignKeyAttribute value => FormatForeignKeyAttribute(value),
        RelationAttribute value => FormatRelationAttribute(value),
        IndexAttribute value => $"new global::DataLinq.Attributes.IndexAttribute({FormatStringLiteral(value.Name)}, global::DataLinq.Attributes.IndexCharacteristic.{value.Characteristic}, global::DataLinq.Attributes.IndexType.{value.Type}{FormatOptionalStringArguments(value.Columns)})",
        InterfaceAttribute value => value.Name is null
            ? $"new global::DataLinq.Attributes.InterfaceAttribute({FormatBool(value.GenerateInterface)})"
            : $"new global::DataLinq.Attributes.InterfaceAttribute({FormatStringLiteral(value.Name)}, {FormatBool(value.GenerateInterface)})",
        DefaultSqlAttribute value => $"new global::DataLinq.Attributes.DefaultSqlAttribute(global::DataLinq.DatabaseType.{value.DatabaseType}, {FormatStringLiteral(value.Expression)})",
        DefaultCurrentTimestampAttribute => "new global::DataLinq.Attributes.DefaultCurrentTimestampAttribute()",
        DefaultNewUUIDAttribute value => $"new global::DataLinq.Attributes.DefaultNewUUIDAttribute(global::DataLinq.Attributes.UUIDVersion.{value.Version})",
        DefaultAttribute value => value.CodeExpression is null
            ? $"new global::DataLinq.Attributes.DefaultAttribute({FormatValueLiteral(value.Value)})"
            : $"new global::DataLinq.Attributes.DefaultAttribute({FormatValueLiteral(value.Value)}, {FormatStringLiteral(value.CodeExpression)})",
        _ => throw new NotSupportedException($"Generated metadata does not support attribute type '{attribute.GetType().FullName}'.")
    };

    private static string FormatForeignKeyAttribute(ForeignKeyAttribute value)
    {
        if (value.Ordinal.HasValue)
        {
            if (value.OnUpdate != ReferentialAction.Unspecified || value.OnDelete != ReferentialAction.Unspecified)
            {
                return $"new global::DataLinq.Attributes.ForeignKeyAttribute({FormatStringLiteral(value.Table)}, {FormatStringLiteral(value.Column)}, {FormatStringLiteral(value.Name)}, {value.Ordinal.Value.ToString(CultureInfo.InvariantCulture)}, global::DataLinq.Attributes.ReferentialAction.{value.OnUpdate}, global::DataLinq.Attributes.ReferentialAction.{value.OnDelete})";
            }

            return $"new global::DataLinq.Attributes.ForeignKeyAttribute({FormatStringLiteral(value.Table)}, {FormatStringLiteral(value.Column)}, {FormatStringLiteral(value.Name)}, {value.Ordinal.Value.ToString(CultureInfo.InvariantCulture)})";
        }

        if (value.OnUpdate != ReferentialAction.Unspecified || value.OnDelete != ReferentialAction.Unspecified)
        {
            return $"new global::DataLinq.Attributes.ForeignKeyAttribute({FormatStringLiteral(value.Table)}, {FormatStringLiteral(value.Column)}, {FormatStringLiteral(value.Name)}, global::DataLinq.Attributes.ReferentialAction.{value.OnUpdate}, global::DataLinq.Attributes.ReferentialAction.{value.OnDelete})";
        }

        return $"new global::DataLinq.Attributes.ForeignKeyAttribute({FormatStringLiteral(value.Table)}, {FormatStringLiteral(value.Column)}, {FormatStringLiteral(value.Name)})";
    }

    private static string FormatRelationAttribute(RelationAttribute value)
    {
        if (value.Columns.Length == 1)
            return $"new global::DataLinq.Attributes.RelationAttribute({FormatStringLiteral(value.Table)}, {FormatStringLiteral(value.Columns[0])}, {FormatNullableStringLiteral(value.Name)})";

        return $"new global::DataLinq.Attributes.RelationAttribute({FormatStringLiteral(value.Table)}, new string[] {{ {FormatStringArguments(value.Columns)} }}, {FormatNullableStringLiteral(value.Name)})";
    }

    private static string FormatNullableEnumProperty(EnumProperty? enumProperty)
    {
        if (!enumProperty.HasValue)
            return "null";

        var value = enumProperty.Value;
        return $"new global::DataLinq.Metadata.EnumProperty({FormatEnumTupleArray(value.DbEnumValues)}, {FormatEnumTupleArray(value.CsEnumValues)}, {FormatBool(value.DeclaredInClass)}, {FormatBool(value.DeclaredInModelFile)})";
    }

    private static string FormatEnumTupleArray(IReadOnlyList<(string name, int value)> values) =>
        $"new (string name, int value)[] {{ {string.Join(", ", values.Select(value => $"({FormatStringLiteral(value.name)}, {value.value.ToString(CultureInfo.InvariantCulture)})"))} }}";

    private static string FormatValueLiteral(object value) => value switch
    {
        string stringValue => FormatStringLiteral(stringValue),
        char charValue => CSharpLiteralFormatter.FormatChar(charValue),
        bool boolValue => FormatBool(boolValue),
        sbyte sbyteValue => $"(sbyte){sbyteValue.ToString(CultureInfo.InvariantCulture)}",
        byte byteValue => $"(byte){byteValue.ToString(CultureInfo.InvariantCulture)}",
        short shortValue => $"(short){shortValue.ToString(CultureInfo.InvariantCulture)}",
        ushort ushortValue => $"(ushort){ushortValue.ToString(CultureInfo.InvariantCulture)}",
        int intValue => intValue.ToString(CultureInfo.InvariantCulture),
        uint uintValue => $"{uintValue.ToString(CultureInfo.InvariantCulture)}U",
        long longValue => FormatLong(longValue),
        ulong ulongValue => $"{ulongValue.ToString(CultureInfo.InvariantCulture)}UL",
        float floatValue => $"{floatValue.ToString("R", CultureInfo.InvariantCulture)}F",
        double doubleValue => $"{doubleValue.ToString("R", CultureInfo.InvariantCulture)}D",
        decimal decimalValue => $"{decimalValue.ToString(CultureInfo.InvariantCulture)}M",
        DateTime dateTime => $"new global::System.DateTime({dateTime.Ticks.ToString(CultureInfo.InvariantCulture)}L, global::System.DateTimeKind.{dateTime.Kind})",
        DateTimeOffset dateTimeOffset => $"new global::System.DateTimeOffset({dateTimeOffset.Ticks.ToString(CultureInfo.InvariantCulture)}L, global::System.TimeSpan.FromTicks({dateTimeOffset.Offset.Ticks.ToString(CultureInfo.InvariantCulture)}L))",
        TimeSpan timeSpan => $"global::System.TimeSpan.FromTicks({timeSpan.Ticks.ToString(CultureInfo.InvariantCulture)}L)",
        Guid guid => $"new global::System.Guid({FormatStringLiteral(guid.ToString())})",
        DynamicFunctions dynamicFunction => $"global::DataLinq.Attributes.DynamicFunctions.{dynamicFunction}",
        UUIDVersion uuidVersion => $"global::DataLinq.Attributes.UUIDVersion.{uuidVersion}",
        _ when value.GetType().FullName == "System.DateOnly" => FormatDateOnlyLiteral(value),
        _ when value.GetType().FullName == "System.TimeOnly" => FormatTimeOnlyLiteral(value),
        _ when value.GetType().IsEnum => $"({FormatGlobalRuntimeTypeName(value.GetType())}){Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
        _ => throw new NotSupportedException($"Generated metadata does not support literal value type '{value.GetType().FullName}'.")
    };

    private static string FormatDateOnlyLiteral(object value)
    {
        var type = value.GetType();
        var year = (int)type.GetProperty("Year")!.GetValue(value)!;
        var month = (int)type.GetProperty("Month")!.GetValue(value)!;
        var day = (int)type.GetProperty("Day")!.GetValue(value)!;

        return $"new global::System.DateOnly({year.ToString(CultureInfo.InvariantCulture)}, {month.ToString(CultureInfo.InvariantCulture)}, {day.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string FormatTimeOnlyLiteral(object value)
    {
        var ticks = (long)value.GetType().GetProperty("Ticks")!.GetValue(value)!;
        return $"new global::System.TimeOnly({ticks.ToString(CultureInfo.InvariantCulture)}L)";
    }

    private static string FormatGlobalRuntimeTypeName(Type type)
    {
        if (type.IsNested && type.DeclaringType is not null)
            return $"{FormatGlobalRuntimeTypeName(type.DeclaringType)}.{type.Name}";

        return string.IsNullOrWhiteSpace(type.Namespace)
            ? $"global::{type.Name}"
            : $"global::{type.Namespace}.{type.Name}";
    }

    private static string FormatStringArguments(IEnumerable<string> values) =>
        string.Join(", ", values.Select(FormatStringLiteral));

    private static string FormatOptionalStringArguments(IReadOnlyCollection<string> values) =>
        values.Count == 0
            ? string.Empty
            : $", {FormatStringArguments(values)}";

    private static string FormatStringLiteral(string value) =>
        CSharpLiteralFormatter.FormatString(value);

    private static string FormatNullableStringLiteral(string? value) =>
        value is null ? "null" : FormatStringLiteral(value);

    private static string GetGeneratedColumnIndexName(ValueProperty property) =>
        $"DataLinqColumnIndex_{GetGeneratedIdentifierSuffix(property.PropertyName)}";

    private static string GetGeneratedColumnHandleName(ValueProperty property) =>
        $"DataLinqColumn_{GetGeneratedIdentifierSuffix(property.PropertyName)}";

    private static string GetGeneratedRelationHandleName(RelationProperty property) =>
        $"DataLinqRelation_{GetGeneratedIdentifierSuffix(property.PropertyName)}";

    private static string? GetProviderKeyRowStoreAccessorExpression(ModelDefinition model)
    {
        if (model.Table.Type != TableType.Table || model.Table.PrimaryKeyColumns.Length == 0)
            return null;

        return $"new {GetGlobalTypeName(model.CsType)}.DataLinqProviderKeyRowStoreAccessor()";
    }

    private static string GetGeneratedIdentifierSuffix(string name)
    {
        var chars = name
            .Select(static character => IsAsciiIdentifierCharacter(character) ? character : '_')
            .ToArray();

        var suffix = new string(chars);
        if (string.IsNullOrEmpty(suffix) || char.IsDigit(suffix[0]))
            return $"_{suffix}";

        return suffix;
    }

    private static bool IsAsciiIdentifierCharacter(char character) =>
        character == '_'
        || (character >= '0' && character <= '9')
        || (character >= 'A' && character <= 'Z')
        || (character >= 'a' && character <= 'z');

    private static string FormatBool(bool value) =>
        value ? "true" : "false";

    private static string FormatNullableBool(bool? value) =>
        value.HasValue ? FormatBool(value.Value) : "null";

    private static string FormatNullableInt(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";

    private static string FormatLong(long value) =>
        $"{value.ToString(CultureInfo.InvariantCulture)}L";

    private static string FormatNullableULong(ulong? value) =>
        value.HasValue ? $"{value.Value.ToString(CultureInfo.InvariantCulture)}UL" : "(ulong?)null";

    private static string FormatNullableUInt(uint? value) =>
        value.HasValue ? $"{value.Value.ToString(CultureInfo.InvariantCulture)}U" : "(uint?)null";

    private string GetFilePath(TableModel table)
    {
        var path = $"{table.Model.CsType.Name}.cs";

        if (Options.SeparateTablesAndViews)
            return table.Table.Type == TableType.Table
                ? $"Tables{Path.DirectorySeparatorChar}{path}"
                : $"Views{Path.DirectorySeparatorChar}{path}";

        return path;
    }

    private IEnumerable<string> WriteInterface(ModelDefinition model, CsTypeDeclaration modelInterface, GeneratorFileFactoryOptions options, List<ValueProperty> valueProps)
    {
        foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(model.Attributes), namespaceTab))
            yield return row;

        yield return $"{namespaceTab}public partial interface {modelInterface.Name}: IModelInstance<{model.Database.CsType.Name}>";
        yield return namespaceTab + "{";

        foreach (var valueProperty in valueProps)
        {
            var prefix = valueProperty.EnumProperty != null && valueProperty.EnumProperty.Value.DeclaredInClass
                ? $"{model.CsType.Name}."
                : "";

            foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(valueProperty.Attributes), $"{namespaceTab}{tab}"))
                yield return row;

            yield return $"{namespaceTab}{tab}{prefix}{valueProperty.CsType.Name}{GetInterfacePropertyNullable(valueProperty)} {valueProperty.PropertyName} {{ get; }}";
        }

        if (model.Table.Type == TableType.Table)
        {
            yield return "";
            yield return $"{namespaceTab}{tab}Mutable{model.CsType.Name} Mutate() => this switch";
            yield return $"{namespaceTab}{tab}{{";
            yield return $"{namespaceTab}{tab}{tab}Mutable{model.CsType.Name} mutable => mutable,";
            yield return $"{namespaceTab}{tab}{tab}Immutable{model.CsType.Name} immutable => immutable.Mutate(),";
            yield return $"{namespaceTab}{tab}{tab}_ => throw new NotSupportedException($\"Call to 'Mutate' not supported for type '{{GetType()}}'\")";
            yield return $"{namespaceTab}{tab}}};";
            yield return "";
            yield return $"{namespaceTab}{tab}Mutable{model.CsType.Name} Mutate(Action<Mutable{model.CsType.Name}> changes) => this switch";
            yield return $"{namespaceTab}{tab}{{";
            yield return $"{namespaceTab}{tab}{tab}Mutable{model.CsType.Name} mutable => mutable.Mutate(changes),";
            yield return $"{namespaceTab}{tab}{tab}Immutable{model.CsType.Name} immutable => immutable.Mutate(changes),";
            yield return $"{namespaceTab}{tab}{tab}_ => throw new NotSupportedException($\"Call to 'Mutate' not supported for type '{{GetType()}}'\")";
            yield return $"{namespaceTab}{tab}}};";
        }

        yield return namespaceTab + "}";
        yield return "";
    }

    private IEnumerable<string> WriteBaseClassPartial(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
        foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(model.Attributes), namespaceTab))
            yield return row;

        yield return $"{namespaceTab}public abstract partial {(options.UseRecords ? "record" : "class")} {model.CsType.Name}{(model.ModelInstanceInterface != null ? $": {model.ModelInstanceInterface.Value.Name}" : "")}";
        yield return namespaceTab + "{";
        yield return $"{namespaceTab}{tab}private static global::DataLinq.Metadata.ModelDefinition DataLinqGeneratedModel = null!;";
        yield return $"{namespaceTab}{tab}internal static global::DataLinq.Metadata.ModelDefinition DataLinqModel => DataLinqGeneratedModel ?? throw new global::System.InvalidOperationException(\"Generated DataLinq metadata for '{model.CsType.Name}' has not been initialized. Initialize a generated DataLinq provider before creating mutable instances.\");";
        yield return "";

        foreach (var valueProperty in model.ValueProperties.Values.OrderBy(x => x.PropertyName, StringComparer.Ordinal))
        {
            yield return $"{namespaceTab}{tab}protected const int {GetGeneratedColumnIndexName(valueProperty)} = {valueProperty.Column.Index.ToString(CultureInfo.InvariantCulture)};";
            yield return $"{namespaceTab}{tab}internal static global::DataLinq.Metadata.ColumnDefinition {GetGeneratedColumnHandleName(valueProperty)} {{ get; private set; }} = null!;";
        }

        if (model.ValueProperties.Any())
            yield return "";

        foreach (var relationProperty in model.RelationProperties.Values.OrderBy(x => x.PropertyName, StringComparer.Ordinal))
            yield return $"{namespaceTab}{tab}internal static global::DataLinq.Metadata.RelationProperty {GetGeneratedRelationHandleName(relationProperty)} {{ get; private set; }} = null!;";

        if (model.RelationProperties.Any())
            yield return "";

        yield return $"{namespaceTab}{tab}internal static void SetDataLinqGeneratedModel(global::DataLinq.Metadata.ModelDefinition model)";
        yield return $"{namespaceTab}{tab}" + "{";
        yield return $"{namespaceTab}{tab}{tab}DataLinqGeneratedModel = model ?? throw new global::System.ArgumentNullException(nameof(model));";

        foreach (var valueProperty in model.ValueProperties.Values.OrderBy(x => x.PropertyName, StringComparer.Ordinal))
            yield return $"{namespaceTab}{tab}{tab}{GetGeneratedColumnHandleName(valueProperty)} = model.ValueProperties[{FormatStringLiteral(valueProperty.PropertyName)}].Column;";

        foreach (var relationProperty in model.RelationProperties.Values.OrderBy(x => x.PropertyName, StringComparer.Ordinal))
            yield return $"{namespaceTab}{tab}{tab}{GetGeneratedRelationHandleName(relationProperty)} = model.RelationProperties[{FormatStringLiteral(relationProperty.PropertyName)}];";

        yield return $"{namespaceTab}{tab}" + "}";
        yield return "";

        if (model.Table.Type == TableType.Table)
        {
            if (model.Table.PrimaryKeyColumns.Length > 0)
            {
                var primaryKeys = model.Table.PrimaryKeyColumns
                    .Select(c => c.ValueProperty)
                    .ToList();

                var keyString = primaryKeys
                    .Select(x => $"{x.CsType.Name} {x.PropertyName.ToCamelCase()}")
                    .ToJoinedString(", ");

                var keyValues = primaryKeys
                    .Select(x => $"{x.PropertyName.ToCamelCase()}")
                    .ToJoinedString(", ");

                var keyTypeName = primaryKeys.Count == 1
                    ? primaryKeys[0].CsType.Name
                    : "DataLinqPrimaryKey";

                if (primaryKeys.Count > 1)
                {
                    var keyTypeParameters = primaryKeys
                        .Select(x => $"{x.CsType.Name} {x.PropertyName.ToCamelCase()}")
                        .ToJoinedString(", ");

                    yield return $"{namespaceTab}{tab}internal readonly record struct {keyTypeName}({keyTypeParameters}) : IProviderKey";
                    yield return $"{namespaceTab}{tab}" + "{";
                    yield return $"{namespaceTab}{tab}{tab}public int ValueCount => {primaryKeys.Count.ToString(CultureInfo.InvariantCulture)};";
                    yield return $"{namespaceTab}{tab}{tab}public object? GetValue(int index) => index switch";
                    yield return $"{namespaceTab}{tab}{tab}" + "{";
                    for (var i = 0; i < primaryKeys.Count; i++)
                        yield return $"{namespaceTab}{tab}{tab}{tab}{i.ToString(CultureInfo.InvariantCulture)} => {primaryKeys[i].PropertyName.ToCamelCase()},";
                    yield return $"{namespaceTab}{tab}{tab}{tab}_ => throw new global::System.IndexOutOfRangeException(),";
                    yield return $"{namespaceTab}{tab}{tab}" + "};";
                    yield return "";
                    yield return $"{namespaceTab}{tab}{tab}public static bool TryCreate(global::DataLinq.Instances.DataLinqKey key, out {keyTypeName} providerKey)";
                    yield return $"{namespaceTab}{tab}{tab}" + "{";
                    yield return $"{namespaceTab}{tab}{tab}{tab}if (key.ValueCount == {primaryKeys.Count.ToString(CultureInfo.InvariantCulture)}";
                    for (var i = 0; i < primaryKeys.Count; i++)
                        yield return $"{namespaceTab}{tab}{tab}{tab}    && key.GetValue({i.ToString(CultureInfo.InvariantCulture)}) is {primaryKeys[i].CsType.Name} {primaryKeys[i].PropertyName.ToCamelCase()}";
                    yield return $"{namespaceTab}{tab}{tab}{tab})";
                    yield return $"{namespaceTab}{tab}{tab}{tab}" + "{";
                    yield return $"{namespaceTab}{tab}{tab}{tab}{tab}providerKey = new {keyTypeName}({keyValues});";
                    yield return $"{namespaceTab}{tab}{tab}{tab}{tab}return true;";
                    yield return $"{namespaceTab}{tab}{tab}{tab}" + "}";
                    yield return "";
                    yield return $"{namespaceTab}{tab}{tab}{tab}providerKey = default;";
                    yield return $"{namespaceTab}{tab}{tab}{tab}return false;";
                    yield return $"{namespaceTab}{tab}{tab}" + "}";
                    yield return $"{namespaceTab}{tab}" + "}";
                    yield return "";
                }

                yield return $"{namespaceTab}{tab}internal static bool TryCreateDataLinqPrimaryKey(IRowData rowData, out {keyTypeName} providerKey)";
                yield return $"{namespaceTab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}if (rowData is not null";
                for (var i = 0; i < primaryKeys.Count; i++)
                    yield return $"{namespaceTab}{tab}{tab}{tab}&& rowData.GetValue({GetGeneratedColumnIndexName(primaryKeys[i])}) is {primaryKeys[i].CsType.Name} {primaryKeys[i].PropertyName.ToCamelCase()}";
                yield return $"{namespaceTab}{tab}{tab}{tab})";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                var providerKeyExpression = primaryKeys.Count == 1
                    ? keyValues
                    : $"new {keyTypeName}({keyValues})";
                yield return $"{namespaceTab}{tab}{tab}{tab}providerKey = {providerKeyExpression};";
                yield return $"{namespaceTab}{tab}{tab}{tab}return true;";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}providerKey = default!;";
                yield return $"{namespaceTab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}" + "}";
                yield return "";

                yield return $"{namespaceTab}{tab}internal static bool TryCreateDataLinqPrimaryKey(global::DataLinq.IDataLinqDataReader reader, global::System.Collections.Generic.IReadOnlyList<int> primaryKeyOrdinals, out {keyTypeName} providerKey)";
                yield return $"{namespaceTab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}if (primaryKeyOrdinals.Count == {primaryKeys.Count.ToString(CultureInfo.InvariantCulture)}";
                for (var i = 0; i < primaryKeys.Count; i++)
                    yield return $"{namespaceTab}{tab}{tab}{tab}&& reader.GetValue<{primaryKeys[i].CsType.Name}>({GetGeneratedColumnHandleName(primaryKeys[i])}, primaryKeyOrdinals[{i.ToString(CultureInfo.InvariantCulture)}]) is {primaryKeys[i].CsType.Name} {primaryKeys[i].PropertyName.ToCamelCase()}";
                yield return $"{namespaceTab}{tab}{tab}{tab})";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}providerKey = {providerKeyExpression};";
                yield return $"{namespaceTab}{tab}{tab}{tab}return true;";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}providerKey = default!;";
                yield return $"{namespaceTab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}" + "}";
                yield return "";

                yield return $"{namespaceTab}{tab}internal static bool TryCreateDataLinqPrimaryKey(global::DataLinq.Instances.DataLinqKey key, out {keyTypeName} providerKey)";
                yield return $"{namespaceTab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}if (key.ValueCount == {primaryKeys.Count.ToString(CultureInfo.InvariantCulture)}";
                for (var i = 0; i < primaryKeys.Count; i++)
                    yield return $"{namespaceTab}{tab}{tab}{tab}&& key.GetValue({i.ToString(CultureInfo.InvariantCulture)}) is {primaryKeys[i].CsType.Name} {primaryKeys[i].PropertyName.ToCamelCase()}";
                yield return $"{namespaceTab}{tab}{tab}{tab})";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}providerKey = {providerKeyExpression};";
                yield return $"{namespaceTab}{tab}{tab}{tab}return true;";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}providerKey = default!;";
                yield return $"{namespaceTab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}" + "}";
                yield return "";

                yield return $"{namespaceTab}{tab}internal static bool TryCreateDataLinqPrimaryKey(global::DataLinq.Instances.IModelInstance model, out {keyTypeName} providerKey)";
                yield return $"{namespaceTab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}if (model is not null";
                for (var i = 0; i < primaryKeys.Count; i++)
                    yield return $"{namespaceTab}{tab}{tab}{tab}&& model[{GetGeneratedColumnHandleName(primaryKeys[i])}] is {primaryKeys[i].CsType.Name} {primaryKeys[i].PropertyName.ToCamelCase()}";
                yield return $"{namespaceTab}{tab}{tab}{tab})";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}providerKey = {providerKeyExpression};";
                yield return $"{namespaceTab}{tab}{tab}{tab}return true;";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}providerKey = default!;";
                yield return $"{namespaceTab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}" + "}";
                yield return "";

                yield return $"{namespaceTab}{tab}internal sealed class DataLinqProviderKeyRowStoreAccessor : global::DataLinq.Instances.IProviderKeyDataReaderRowStoreAccessor";
                yield return $"{namespaceTab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}public bool TryAddRow(global::DataLinq.Cache.RowCache cache, global::DataLinq.Instances.RowData rowData, global::DataLinq.Instances.IImmutableInstance row)";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}if (!TryCreateDataLinqPrimaryKey(rowData, out var providerKey))";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}return false;";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}{tab}return cache.TryAddRow(providerKey, rowData.Size, row);";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}public bool TryGetRow(global::DataLinq.Cache.RowCache cache, global::DataLinq.Instances.DataLinqKey key, out global::DataLinq.Instances.IImmutableInstance? row)";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}if (!TryCreateDataLinqPrimaryKey(key, out var providerKey))";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}row = null;";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}{tab}return cache.TryGetValue(providerKey, out row);";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";

                yield return $"{namespaceTab}{tab}{tab}public bool TryGetRow(global::DataLinq.Cache.TableCache tableCache, global::DataLinq.IDataLinqDataReader reader, global::System.Collections.Generic.IReadOnlyList<int> primaryKeyOrdinals, global::DataLinq.Interfaces.IDataSourceAccess dataSource, out global::DataLinq.Instances.IImmutableInstance? row)";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}if (!TryCreateDataLinqPrimaryKey(reader, primaryKeyOrdinals, out var providerKey))";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}row = null;";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}{tab}row = tableCache.GetRow(providerKey, dataSource);";
                yield return $"{namespaceTab}{tab}{tab}{tab}return true;";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}public bool TryRemoveRow(global::DataLinq.Cache.RowCache cache, global::DataLinq.Instances.DataLinqKey key, out int numRowsRemoved)";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}if (!TryCreateDataLinqPrimaryKey(key, out var providerKey))";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}numRowsRemoved = 0;";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}{tab}return cache.TryRemoveProviderKey(providerKey, out numRowsRemoved);";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                var dynamicKeyExpression = primaryKeys.Count == 1
                    ? "global::DataLinq.Instances.DataLinqKey.FromValue(providerKey)"
                    : "global::DataLinq.Instances.DataLinqKey.FromProviderKey(providerKey)";
                yield return $"{namespaceTab}{tab}{tab}public bool TryCreateKey(global::DataLinq.Instances.IRowData rowData, out global::DataLinq.Instances.DataLinqKey key)";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}if (TryCreateDataLinqPrimaryKey(rowData, out var providerKey))";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}key = {dynamicKeyExpression};";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}return true;";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}{tab}key = default;";
                yield return $"{namespaceTab}{tab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}public bool TryCreateKey(global::DataLinq.Instances.IModelInstance model, out global::DataLinq.Instances.DataLinqKey key)";
                yield return $"{namespaceTab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}if (TryCreateDataLinqPrimaryKey(model, out var providerKey))";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "{";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}key = {dynamicKeyExpression};";
                yield return $"{namespaceTab}{tab}{tab}{tab}{tab}return true;";
                yield return $"{namespaceTab}{tab}{tab}{tab}" + "}";
                yield return "";
                yield return $"{namespaceTab}{tab}{tab}{tab}key = default;";
                yield return $"{namespaceTab}{tab}{tab}{tab}return false;";
                yield return $"{namespaceTab}{tab}{tab}" + "}";
                yield return $"{namespaceTab}{tab}" + "}";
                yield return "";

                if (primaryKeys.Count == 1)
                {
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, IDataSourceAccess dataSource) => IImmutable<{model.CsType.Name}>.GetByProviderKey({keyValues}, dataSource);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Database<{model.Database.CsType.Name}> database) => IImmutable<{model.CsType.Name}>.GetByProviderKey({keyValues}, database.Provider.ReadOnlyAccess);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Transaction<{model.Database.CsType.Name}> transaction) => IImmutable<{model.CsType.Name}>.GetByProviderKey({keyValues}, transaction);";
                }
                else
                {
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, IDataSourceAccess dataSource) => IImmutable<{model.CsType.Name}>.GetByProviderKey(new {keyTypeName}({keyValues}), dataSource);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Database<{model.Database.CsType.Name}> database) => IImmutable<{model.CsType.Name}>.GetByProviderKey(new {keyTypeName}({keyValues}), database.Provider.ReadOnlyAccess);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Transaction<{model.Database.CsType.Name}> transaction) => IImmutable<{model.CsType.Name}>.GetByProviderKey(new {keyTypeName}({keyValues}), transaction);";
                }

                yield return $"";
            }


            var requiredProps = GetRequiredValueProperties(model);

            if (requiredProps.Any())
            {
                var constructorParams = requiredProps.Select(GetConstructorParam).ToJoinedString(", ");
                var constructorArgs = requiredProps.Select(v => v.Column.ValueProperty.PropertyName.ToCamelCase()).ToJoinedString(", ");

                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({constructorParams}) => new({constructorArgs});";
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({constructorParams}, Action<Mutable{model.CsType.Name}> changes) => new Mutable{model.CsType.Name}({constructorArgs}).Mutate(changes);";
            }
            else
            {
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate() => new();";
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(Action<Mutable{model.CsType.Name}> changes) => new Mutable{model.CsType.Name}().Mutate(changes);";
            }

            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.CsType.Name} model) => new Mutable{model.CsType.Name}(model);";
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) => new Mutable{model.CsType.Name}(model).Mutate(changes);";

            if (model.ModelInstanceInterface != null)
            {
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.ModelInstanceInterface.Value.Name} model) => model.Mutate();";
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.ModelInstanceInterface.Value.Name} model, Action<Mutable{model.CsType.Name}> changes) => model.Mutate(changes);";
            }
        }

        yield return namespaceTab + "}";
        yield return "";
    }

    private IEnumerable<string> ImmutableModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options, List<ValueProperty> valueProps, List<RelationProperty> relationProps)
    {
        foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(model.Attributes), namespaceTab))
            yield return row;

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Immutable{model.CsType.Name}(IRowData rowData, IDataSourceAccess dataSource) : {model.CsType.Name}(rowData, dataSource)";
        yield return namespaceTab + "{";
        yield return $"{namespaceTab}{tab}public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) => new Immutable{model.CsType.Name}(rowData, dataSource);";
        yield return "";

        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;

            yield return $"{namespaceTab}{tab}private {GetCsTypeName(c.ValueProperty)}{GetImmutableFieldNullable(c.ValueProperty)} _{c.ValueProperty.PropertyName};";

            foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(c.ValueProperty.Attributes), $"{namespaceTab}{tab}"))
                yield return row;

            yield return $"{namespaceTab}{tab}public override {GetCsTypeName(c.ValueProperty)}{GetImmutablePropertyNullable(c.ValueProperty)} {c.ValueProperty.PropertyName} => _{c.ValueProperty.PropertyName} ??= ({GetCsTypeName(c.ValueProperty)}{GetImmutablePropertyNullable(c.ValueProperty)}){(IsImmutableGetterNullable(valueProperty) ? "GetNullableValue" : "GetValue")}({GetGeneratedColumnIndexName(valueProperty)});";
            yield return $"";
        }

        foreach (var relationProperty in relationProps)
        {
            var otherPart = relationProperty.RelationPart.GetOtherSide();

            if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
            {
                var nullableChar = relationProperty.CsNullable ? "?" : "";
                // Conditionally add parentheses and the null-forgiving operator.
                var expressionPrefix = (Options.UseNullableReferenceTypes && !relationProperty.CsNullable) ? "(" : "";
                var expressionSuffix = (Options.UseNullableReferenceTypes && !relationProperty.CsNullable) ? ")!" : "";

                yield return $"{namespaceTab}{tab}private IImmutableForeignKey<{otherPart.ColumnIndex.Table.Model.CsType.Name}>{GetUseNullableReferenceTypes()} _{relationProperty.PropertyName};";
                yield return $"{namespaceTab}{tab}public override {otherPart.ColumnIndex.Table.Model.CsType.Name}{nullableChar} {relationProperty.PropertyName} => {expressionPrefix}(_{relationProperty.PropertyName} ??= {GetImmutableForeignKeyExpression(relationProperty, otherPart.ColumnIndex.Table.Model.CsType.Name)}).Value{expressionSuffix};";
            }
            else
            {
                yield return $"{namespaceTab}{tab}private IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}>{GetUseNullableReferenceTypes()} _{relationProperty.PropertyName};";
                yield return $"{namespaceTab}{tab}public override IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}> {relationProperty.PropertyName} => _{relationProperty.PropertyName} ??= {GetImmutableRelationExpression(relationProperty, otherPart.ColumnIndex.Table.Model.CsType.Name)};";
            }

            yield return $"";
        }

        //if (model.Table.Type == TableType.Table)
        //    yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name} Mutate() => new(this);";

        yield return namespaceTab + "}";
    }

    private string GetImmutableRelationExpression(RelationProperty relationProperty, string relatedModelName)
    {
        if (!TryGetScalarProviderRelationKey(relationProperty, out var keyProperty))
            return $"GetImmutableRelation<{relatedModelName}>({GetGeneratedRelationHandleName(relationProperty)})";

        return GetProviderRelationExpression(
            keyProperty,
            $"GetImmutableRelation<{relatedModelName}, {GetCsTypeName(keyProperty)}>",
            $"GetImmutableRelationFromKey<{relatedModelName}>",
            GetGeneratedRelationHandleName(relationProperty));
    }

    private string GetImmutableForeignKeyExpression(RelationProperty relationProperty, string relatedModelName)
    {
        if (!TryGetScalarProviderRelationKey(relationProperty, out var keyProperty))
            return $"GetImmutableForeignKey<{relatedModelName}>({GetGeneratedRelationHandleName(relationProperty)})";

        return GetProviderRelationExpression(
            keyProperty,
            $"GetImmutableForeignKey<{relatedModelName}, {GetCsTypeName(keyProperty)}>",
            $"GetImmutableForeignKeyFromKey<{relatedModelName}>",
            GetGeneratedRelationHandleName(relationProperty));
    }

    private string GetProviderRelationExpression(
        ValueProperty keyProperty,
        string typedMethodName,
        string dynamicMethodName,
        string relationHandleName)
    {
        var propertyName = keyProperty.PropertyName;
        if (!IsImmutablePropertyNullable(keyProperty))
            return $"{typedMethodName}({propertyName}, {relationHandleName})";

        if (MetadataTypeConverter.IsCsTypeNullable(keyProperty.CsType.Name))
            return $"{propertyName}.HasValue ? {typedMethodName}({propertyName}.Value, {relationHandleName}) : {dynamicMethodName}(global::DataLinq.Instances.DataLinqKey.Null, {relationHandleName})";

        return $"{propertyName} is not null ? {typedMethodName}({propertyName}, {relationHandleName}) : {dynamicMethodName}(global::DataLinq.Instances.DataLinqKey.Null, {relationHandleName})";
    }

    private static bool TryGetScalarProviderRelationKey(RelationProperty relationProperty, out ValueProperty keyProperty)
    {
        var columns = relationProperty.RelationPart.ColumnIndex.Columns;
        if (columns.Count == 1 &&
            TableKeyShape.GetProviderStoreKind(columns[0]) != TableKeyComponentStoreKind.Unsupported)
        {
            keyProperty = columns[0].ValueProperty;
            return true;
        }

        keyProperty = null!;
        return false;
    }

    private IEnumerable<string> MutableModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options, List<ValueProperty> valueProps, List<RelationProperty> relationProps)
    {
        List<string> interfaces = [$"IMutableInstance<{model.Database.CsType.Name}>"];

        if (model.ModelInstanceInterface != null)
            interfaces.Add(model.ModelInstanceInterface.Value.Name);

        foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(model.Attributes), namespaceTab))
            yield return row;

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Mutable{model.CsType.Name} : Mutable<{model.CsType.Name}>, {interfaces.ToJoinedString(", ")}";
        yield return namespaceTab + "{";

        var defaultProps = GetDefaultValueProperties(model);

        // Parameterless constructor for users who prefer setting properties via setters.
        yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name}() : base({model.CsType.Name}.DataLinqModel)";
        yield return $"{namespaceTab}{tab}" + "{";

        foreach (var v in defaultProps)
            yield return $"{namespaceTab}{tab}{tab}this.{v.PropertyName} = {v.GetDefaultValueCode()};";

        yield return $"{namespaceTab}{tab}" + "}";

        // Constructor with required properties.
        var requiredProps = GetRequiredValueProperties(model);
        if (requiredProps.Any())
        {
            var paramList = requiredProps.Select(GetConstructorParam).ToJoinedString(", ");

            // Decorate this constructor with the SetsRequiredMembers attribute.
            yield return $"";
            yield return $"{namespaceTab}{tab}[SetsRequiredMembers]";
            yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name}({paramList}) : this()";
            yield return $"{namespaceTab}{tab}" + "{";

            foreach (var v in defaultProps)
                yield return $"{namespaceTab}{tab}{tab}this.{v.PropertyName} = {v.GetDefaultValueCode()};";

            // For each required property, assign the passed parameter to the property.
            foreach (var v in requiredProps)
                yield return $"{namespaceTab}{tab}{tab}this.{v.PropertyName} = {v.PropertyName.ToCamelCase()};";

            yield return $"{namespaceTab}{tab}" + "}";
        }

        // Constructor that accepts an immutable instance.
        yield return $"";
        yield return $"{namespaceTab}{tab}#pragma warning disable CS8618 // We know that the base constructor sets all required properties";
        yield return $"{namespaceTab}{tab}[SetsRequiredMembers]";
        yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name}({model.CsType.Name} immutable{model.CsType.Name}) : base(immutable{model.CsType.Name}) {{}}";
        yield return $"{namespaceTab}{tab}#pragma warning restore CS8618";

        // Generate the properties as before.
        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;
            var nullableAnnotation = GetMutablePropertyNullable(c.ValueProperty);

            // The null-forgiving operator is only needed if NRTs are enabled AND the property is non-nullable.
            var nullForgivingOperator = (Options.UseNullableReferenceTypes && nullableAnnotation == "") ? "!" : "";

            // Determine if the 'new' keyword is needed to hide a base member.
            var newModifier = MutableBaseMethodNames.Contains(valueProperty.PropertyName)
                ? "new "
                : "";

            yield return "";
            foreach (var row in FormatSummaryXmlDocs(GetDocumentationComment(c.ValueProperty.Attributes), $"{namespaceTab}{tab}"))
                yield return row;

            yield return $"{namespaceTab}{tab}public {newModifier}virtual {GetMutablePropertyRequired(c.ValueProperty)}{GetCsTypeName(c.ValueProperty)}{nullableAnnotation} {c.ValueProperty.PropertyName}";
            yield return $"{namespaceTab}{tab}" + "{";
            yield return $"{namespaceTab}{tab}{tab}get => ({GetCsTypeName(c.ValueProperty)}{nullableAnnotation})GetValue({model.CsType.Name}.{GetGeneratedColumnHandleName(valueProperty)}){nullForgivingOperator};";
            yield return $"{namespaceTab}{tab}{tab}set => SetValue({model.CsType.Name}.{GetGeneratedColumnHandleName(valueProperty)}, value);";
            yield return $"{namespaceTab}{tab}" + "}";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ExtensionMethodsFileContents(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
        yield return $"{namespaceTab}public static class {model.CsType.Name}Extensions";
        yield return namespaceTab + "{";

        //Mutate
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(this {model.CsType.Name} model) => model is null";
        yield return $"{namespaceTab}{tab}{tab}? throw new ArgumentNullException(nameof(model))";
        yield return $"{namespaceTab}{tab}{tab}: new(model);";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}if (model is null)";
        yield return $"{namespaceTab}{tab}{tab}{tab}throw new ArgumentNullException(nameof(model));";
        yield return $"{namespaceTab}{tab}{tab}";
        yield return $"{namespaceTab}{tab}{tab}var mutable = model.Mutate();";
        yield return $"{namespaceTab}{tab}{tab}changes(mutable);";
        yield return $"{namespaceTab}{tab}{tab}return mutable;";
        yield return $"{namespaceTab}{tab}}}";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}changes(model);";
        yield return $"{namespaceTab}{tab}{tab}return model;";
        yield return $"{namespaceTab}{tab}}}";

        // First, compute the required constructor parameters and argument list.
        var requiredProps = GetRequiredValueProperties(model);

        // MutateOrNew
        if (requiredProps.Any())
        {
            var constructorParams = requiredProps.Select(GetConstructorParam).ToJoinedString(", ");
            var constructorArgs = requiredProps.Select(v => v.Column.ValueProperty.PropertyName.ToCamelCase()).ToJoinedString(", ");

            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model, {constructorParams}) => model is null ? new Mutable{model.CsType.Name}({constructorArgs}) : model.Mutate(x =>";
            yield return $"{namespaceTab}{tab}{{";
            foreach (var v in requiredProps)
                yield return $"{namespaceTab}{tab}{tab}x.{v.PropertyName} = {v.Column.ValueProperty.PropertyName.ToCamelCase()};";
            yield return $"{namespaceTab}{tab}}});";
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model, {constructorParams}, Action<Mutable{model.CsType.Name}> changes) => model.MutateOrNew({constructorArgs}).Mutate(changes);";
        }
        else
        {
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model) => model is null ? new() : new(model);";
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model, Action<Mutable{model.CsType.Name}> changes) => model is null ? new Mutable{model.CsType.Name}().Mutate(changes) : new Mutable{model.CsType.Name}(model).Mutate(changes);";
        }

        //Insert
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Insert(transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Insert(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert<T>(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Insert(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert(this Transaction transaction, Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Insert(changes, transaction);";

        //Update
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.GetDataSource().Provider.Commit(transaction => model.Update(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Update(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update<T>(this Database<T> database, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Update(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update(this Transaction transaction, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Update(transaction));";

        //Save
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Database<T> database, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Update(model, changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Transaction transaction, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Save(changes, transaction));";

        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Save(transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Save(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel<T> =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Save(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Mutable{model.CsType.Name} model, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Save(model);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Transaction transaction, Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Save(changes, transaction);";

        yield return namespaceTab + "}";
    }

    private string GetConstructorParam(ValueProperty property)
    {
        var typeName = GetCsTypeName(property);
        var nullable = GetMutablePropertyNullable(property);
        var paramName = property.PropertyName.ToCamelCase();

        return $"{typeName}{nullable} {paramName}";
    }

    private List<ValueProperty> GetRequiredValueProperties(ModelDefinition model)
    {
        // Gather the required value properties for the mutable constructor.
        return model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(a => a is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(a => a is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .Where(v => IsMutablePropertyRequired(v.Column.ValueProperty))
            .ToList();
    }

    private List<ValueProperty> GetDefaultValueProperties(ModelDefinition model)
    {
        // Gather the required value properties for the mutable constructor.
        return model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(a => a is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(a => a is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .Where(x => x.Column.ValueProperty.Attributes.Any(a => a is DefaultAttribute))
            .Where(x => x.Column.ValueProperty.GetDefaultAttribute() is not DefaultSqlAttribute)
            .Where(x => !Options.SuppressedDefaultValueProperties.Contains(x))
            .ToList();
    }

    private string GetCsTypeName(ValueProperty property)
    {
        string name = string.Empty;

        if (property.EnumProperty?.DeclaredInClass == true)
            name += $"{property.Model.CsType.Name}.";

        name += property.CsType.Name;

        return name;
    }

    private static string GetGlobalTypeName(CsTypeDeclaration csType)
        => string.IsNullOrWhiteSpace(csType.Namespace)
            ? $"global::{csType.Name}"
            : $"global::{csType.Namespace}.{csType.Name}";

    private static string GetGlobalImmutableTypeName(CsTypeDeclaration csType)
        => string.IsNullOrWhiteSpace(csType.Namespace)
            ? $"global::Immutable{csType.Name}"
            : $"global::{csType.Namespace}.Immutable{csType.Name}";

    private static string GetGlobalMutableTypeName(CsTypeDeclaration csType)
        => string.IsNullOrWhiteSpace(csType.Namespace)
            ? $"global::Mutable{csType.Name}"
            : $"global::{csType.Namespace}.Mutable{csType.Name}";

    private string GetImmutablePropertyNullable(ValueProperty property)
    {
        return IsImmutablePropertyNullable(property) ? "?" : "";
    }

    private string GetMutablePropertyNullable(ValueProperty property)
    {
        return IsInterfacePropertyNullable(property) ? "?" : "";
    }

    private string GetMutablePropertyRequired(ValueProperty property)
    {
        return IsMutablePropertyRequired(property) ? "required " : "";
    }

    private string GetImmutableFieldNullable(ValueProperty property)
    {
        return IsImmutableFieldNullable(property) ? "?" : "";
    }

    private string GetUseNullableReferenceTypes()
    {
        return Options.UseNullableReferenceTypes ? "?" : "";
    }

    private string GetInterfacePropertyNullable(ValueProperty property)
    {
        return IsInterfacePropertyNullable(property) ? "?" : "";
    }

    private bool IsInterfacePropertyNullable(ValueProperty property)
    {
        return (Options.UseNullableReferenceTypes || property.CsNullable) &&
            (property.Column.Nullable || property.Column.AutoIncrement);
    }

    private bool IsMutablePropertyRequired(ValueProperty property)
    {
        // A property is required if it's not nullable, not auto-incrementing, not a default value,
        // AND it's either NOT a foreign key OR it IS a primary key.
        return !property.CsNullable &&
               !property.Column.Nullable &&
               !property.Column.AutoIncrement &&
               (!property.Column.ForeignKey || property.Column.PrimaryKey) &&
               !property.HasDefaultValue();
    }

    private bool IsImmutablePropertyNullable(ValueProperty property)
    {
        return property.CsNullable || property.Column.AutoIncrement;
    }

    private bool IsImmutableGetterNullable(ValueProperty property)
    {
        return !Options.UseNullableReferenceTypes || IsImmutablePropertyNullable(property);
    }

    private bool IsImmutableFieldNullable(ValueProperty property)
    {
        return Options.UseNullableReferenceTypes
            || property.CsNullable
            || property.EnumProperty.HasValue
            || MetadataTypeConverter.IsCsTypeNullable(property.CsType.Name)
            || !MetadataTypeConverter.IsKnownCsType(property.CsType.Name);
    }

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

    private IEnumerable<string> FileHeader(string namespaceName, bool useFileScopedNamespaces, IEnumerable<string> usings)
    {
        foreach (var row in GeneratedFilePreamble.Create(new GeneratedFilePreambleOptions
        {
            UseNullableReferenceTypes = Options.UseNullableReferenceTypes
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
