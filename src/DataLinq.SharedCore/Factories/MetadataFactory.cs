using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public struct MetadataFromDatabaseFactoryOptions
{
    public bool CapitaliseNames { get; set; } = false;
    public bool DeclareEnumsInClass { get; set; } = false;
    public List<string>? Include { get; set; }
    public Action<string>? Log { get; set; }

    public MetadataFromDatabaseFactoryOptions()
    {
    }
}

public static class MetadataFactory
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while"
    };

    public static void ParseInterfaces(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels)
        {
            var model = tableModel.Model;

            if (model.ModelInstanceInterface == null)
            {
                var interfaceName = $"I{model.CsType.Name}";
                model.SetModelInstanceInterface(new CsTypeDeclaration(interfaceName, model.CsType.Namespace, ModelCsType.Interface));
            }
        }
    }

    public static Option<TableDefinition, IDLOptionFailure> ParseTable(ModelDefinition model)
    {
        if (model == null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Model cannot be null");

        TableDefinition table;

        if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("ITableModel")/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new TableDefinition(model.CsType.Name);
        else if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("IViewModel")/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new ViewDefinition(model.CsType.Name);
        else
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Model '{model.CsType.Name}' does not inherit from 'ITableModel' or 'IViewModel'.");

        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                table.SetDbName(tableAttribute.Name);

            if (attribute is ViewAttribute viewAttribute)
                table.SetDbName(viewAttribute.Name);

            if (attribute is UseCacheAttribute useCache)
                table.UseCache = useCache.UseCache;

            if (attribute is CacheLimitAttribute cacheLimit)
                table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                table.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (table is ViewDefinition view && attribute is DefinitionAttribute definitionAttribute)
                view.SetDefinition(definitionAttribute.Sql);
        }

        table.SetColumns(model.ValueProperties.Values.Select(table.ParseColumn));

        return table;
    }

    public static void IndexColumns(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Select(x => x.Table))
            for (var i = 0; i < table.Columns.Length; i++)
                table.Columns[i].SetIndex(i);
    }

    public static Option<bool, IDLOptionFailure> ParseIndices(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            foreach (var indexAttribute in tableModel.Model.Attributes.OfType<IndexAttribute>())
            {
                if (!indexAttribute.Columns.Any())
                    return CreateIndexFailure(
                        tableModel.Model,
                        indexAttribute,
                        $"Class-level index '{indexAttribute.Name}' on table '{tableModel.Table.DbName}' must specify its columns. IndexAttribute.Columns expects database column names.");

                if (!TryResolveIndexColumns(tableModel.Table, indexAttribute, out var columnsForIndex, out var missingColumn))
                    return CreateIndexFailure(
                        tableModel.Model,
                        indexAttribute,
                        $"Index '{indexAttribute.Name}' on table '{tableModel.Table.DbName}' references column '{missingColumn}', but that column does not exist. IndexAttribute.Columns expects database column names, not C# property names.");

                try
                {
                    tableModel.Table.ColumnIndices.Add(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
                }
                catch (InvalidOperationException exception)
                {
                    return CreateIndexFailure(tableModel.Model, indexAttribute, exception.Message);
                }
                catch (ArgumentException exception)
                {
                    return CreateIndexFailure(tableModel.Model, indexAttribute, exception.Message);
                }
            }
        }

        var indices = database.TableModels
            .Where(x => !x.IsStub)
            .SelectMany(tableModel => tableModel.Table.Columns
                .Select(column => (column, indexAttributes: column.ValueProperty.Attributes.OfType<IndexAttribute>().ToList())))
            .Where(t => t.indexAttributes.Any());

        foreach (var (column, indexAttributes) in indices)
        {
            foreach (var indexAttribute in indexAttributes)
            {
                var existingIndex = column.Table.ColumnIndices.FirstOrDefault(x => x.Name == indexAttribute.Name);

                if (existingIndex != null)
                {
                    if (!existingIndex.Columns.Contains(column))
                        existingIndex.AddColumn(column);
                }
                else
                {
                    List<ColumnDefinition> columnsForIndex;
                    if (indexAttribute.Columns.Any())
                    {
                        if (!TryResolveIndexColumns(column.Table, indexAttribute, out columnsForIndex, out var missingColumn))
                            return CreateIndexFailure(
                                column,
                                indexAttribute,
                                $"Index '{indexAttribute.Name}' on table '{column.Table.DbName}' references column '{missingColumn}', but that column does not exist. IndexAttribute.Columns expects database column names, not C# property names.");
                    }
                    else
                    {
                        columnsForIndex = [column];
                    }

                    try
                    {
                        column.Table.ColumnIndices.Add(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
                    }
                    catch (InvalidOperationException exception)
                    {
                        return CreateIndexFailure(column, indexAttribute, exception.Message);
                    }
                    catch (ArgumentException exception)
                    {
                        return CreateIndexFailure(column, indexAttribute, exception.Message);
                    }
                }
            }
        }

        return true;
    }

    private static bool TryResolveIndexColumns(
        TableDefinition table,
        IndexAttribute indexAttribute,
        out List<ColumnDefinition> columnsForIndex,
        out string? missingColumn)
    {
        columnsForIndex = [];
        missingColumn = null;

        foreach (var columnName in indexAttribute.Columns)
        {
            var indexColumn = table.Columns.SingleOrDefault(c => c.DbName == columnName);
            if (indexColumn == null)
            {
                missingColumn = columnName;
                return false;
            }

            columnsForIndex.Add(indexColumn);
        }

        return true;
    }

    private static IDLOptionFailure CreateIndexFailure(ModelDefinition model, IndexAttribute attribute, string message)
    {
        var attributeLocation = model.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var modelLocation = model.GetSourceLocation();
        if (modelLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, modelLocation.Value);

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, model);
    }

    private static IDLOptionFailure CreateIndexFailure(ColumnDefinition column, IndexAttribute attribute, string message)
    {
        var attributeLocation = column.ValueProperty.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var property = column.ValueProperty;
        if (property.SourceInfo.HasValue && property.CsFile.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value));

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, column);
    }

    public static Option<bool, IDLOptionFailure> ValidateUniqueTableNames(DatabaseDefinition database)
    {
        var duplicateGroup = database.TableModels
            .Where(x => !x.IsStub)
            .GroupBy(x => x.Table.DbName, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateGroup == null)
            return true;

        var duplicates = duplicateGroup.ToArray();
        var first = duplicates[0];
        var duplicate = duplicates[1];
        var message = $"Duplicate table definition for '{duplicateGroup.Key}' in database '{database.DbName}'. Models '{first.Model.CsType.Name}' and '{duplicate.Model.CsType.Name}' both map to the same table name.";
        var sourceLocation = GetTableNameSourceLocation(duplicate.Model);

        return sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, duplicate.Model);
    }

    public static Option<bool, IDLOptionFailure> ValidateUniqueTableModelPropertyNames(DatabaseDefinition database)
    {
        var duplicateGroup = database.TableModels
            .GroupBy(x => x.CsPropertyName, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateGroup == null)
            return true;

        var duplicates = duplicateGroup.ToArray();
        var first = duplicates[0];
        var duplicate = duplicates[1];
        var message = $"Duplicate table model property '{duplicateGroup.Key}' in database '{database.DbName}'. Tables '{first.Table.DbName}' and '{duplicate.Table.DbName}' (models '{first.Model.CsType.Name}' and '{duplicate.Model.CsType.Name}') would generate the same database property.";

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, duplicate.Model);
    }

    public static void NormalizeDatabaseTypeName(DatabaseDefinition database)
    {
        var tablePropertyNames = new HashSet<string>(
            database.TableModels.Select(x => x.CsPropertyName),
            StringComparer.Ordinal);
        var csTypeName = database.CsType.Name;

        while (tablePropertyNames.Contains(csTypeName))
            csTypeName = $"{csTypeName}Db";

        if (!string.Equals(database.CsType.Name, csTypeName, StringComparison.Ordinal))
            database.SetCsType(database.CsType.MutateName(csTypeName));
    }

    public static Option<bool, IDLOptionFailure> ValidateCSharpSymbolNames(DatabaseDefinition database)
    {
        var databaseTypeFailure = ValidateCSharpTypeDeclaration(
            database.CsType,
            $"Database '{database.DbName}'",
            database);
        if (databaseTypeFailure is not null)
            return databaseTypeFailure;

        foreach (var tableModel in database.TableModels)
        {
            if (!IsValidCSharpIdentifier(tableModel.CsPropertyName))
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Table '{tableModel.Table.DbName}' uses C# database property name '{tableModel.CsPropertyName}', which is not a valid unescaped C# identifier.",
                    tableModel.Table);

            var model = tableModel.Model;
            var modelTypeFailure = ValidateCSharpTypeDeclaration(
                model.CsType,
                $"Model '{model.CsType.Name}'",
                model);
            if (modelTypeFailure is not null)
                return modelTypeFailure;

            var modelInstanceInterfaceFailure = ValidateOptionalCSharpTypeReference(
                model.ModelInstanceInterface,
                $"Model '{model.CsType.Name}' model-instance interface",
                model);
            if (modelInstanceInterfaceFailure is not null)
                return modelInstanceInterfaceFailure;

            var immutableTypeFailure = ValidateOptionalCSharpTypeReference(
                model.ImmutableType,
                $"Model '{model.CsType.Name}' immutable type",
                model);
            if (immutableTypeFailure is not null)
                return immutableTypeFailure;

            var mutableTypeFailure = ValidateOptionalCSharpTypeReference(
                model.MutableType,
                $"Model '{model.CsType.Name}' mutable type",
                model);
            if (mutableTypeFailure is not null)
                return mutableTypeFailure;

            foreach (var modelUsing in model.Usings)
            {
                if (modelUsing is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' contains a null using namespace.",
                        model);

                if (!IsValidCSharpNamespace(modelUsing.FullNamespaceName))
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' uses C# using namespace '{modelUsing.FullNamespaceName}', which is not a valid unescaped C# namespace.",
                        model);
            }

            foreach (var property in model.ValueProperties.Values)
            {
                var propertyTypeFailure = ValidateCSharpTypeUsage(
                    property.CsType,
                    $"Value property '{GetValuePropertyDisplayName(property)}'",
                    property);
                if (propertyTypeFailure is not null)
                    return propertyTypeFailure;

                if (!IsValidCSharpIdentifier(property.PropertyName))
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' uses C# property name '{property.PropertyName}', which is not a valid unescaped C# identifier.");
            }

            foreach (var property in model.RelationProperties.Values)
            {
                var propertyTypeFailure = ValidateCSharpTypeUsage(
                    property.CsType,
                    $"Relation property '{GetRelationPropertyDisplayName(property)}'",
                    property);
                if (propertyTypeFailure is not null)
                    return propertyTypeFailure;

                if (!IsValidCSharpIdentifier(property.PropertyName))
                    return CreateRelationPropertyFailure(
                        property,
                        null,
                        $"Relation property '{GetRelationPropertyDisplayName(property)}' uses C# property name '{property.PropertyName}', which is not a valid unescaped C# identifier.");
            }
        }

        return true;
    }

    private static IDLOptionFailure? ValidateOptionalCSharpTypeReference(
        CsTypeDeclaration? type,
        string scope,
        IDefinition context)
    {
        if (!type.HasValue)
            return null;

        return ValidateCSharpTypeReference(type.Value, scope, context);
    }

    private static IDLOptionFailure? ValidateCSharpTypeDeclaration(
        CsTypeDeclaration type,
        string scope,
        IDefinition context)
    {
        if (!IsValidCSharpIdentifier(type.Name))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# type name '{type.Name}', which is not a valid unescaped C# identifier.",
                context);

        if (!IsValidCSharpNamespace(type.Namespace))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# namespace '{type.Namespace}', which is not a valid unescaped C# namespace.",
                context);

        return null;
    }

    private static IDLOptionFailure? ValidateCSharpTypeReference(
        CsTypeDeclaration type,
        string scope,
        IDefinition context)
    {
        if (!IsValidCSharpIdentifier(type.Name))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# type name '{type.Name}', which is not a valid unescaped C# identifier.",
                context);

        if (!IsValidCSharpNamespace(type.Namespace))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# namespace '{type.Namespace}', which is not a valid unescaped C# namespace.",
                context);

        return null;
    }

    private static IDLOptionFailure? ValidateCSharpTypeUsage(
        CsTypeDeclaration type,
        string scope,
        IDefinition context)
    {
        if (!IsValidCSharpTypeReferenceName(type.Name))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# type name '{type.Name}', which is not valid C# type syntax.",
                context);

        return null;
    }

    private static bool IsValidCSharpNamespace(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        var namespaceName = value!;

        return namespaceName
            .Split('.')
            .All(IsValidCSharpIdentifier);
    }

    public static Option<bool, IDLOptionFailure> ValidateCacheMetadata(DatabaseDefinition database)
    {
        foreach (var (limitType, amount) in database.CacheLimits)
        {
            var failure = ValidateCacheLimit(
                limitType,
                amount,
                $"Database '{database.DbName}'",
                database);
            if (failure is not null)
                return failure;
        }

        foreach (var (cleanupType, amount) in database.CacheCleanup)
        {
            var failure = ValidateCacheCleanup(
                cleanupType,
                amount,
                $"Database '{database.DbName}'",
                database);
            if (failure is not null)
                return failure;
        }

        foreach (var (indexCacheType, amount) in database.IndexCache)
        {
            var failure = ValidateIndexCachePolicy(
                indexCacheType,
                amount,
                $"Database '{database.DbName}'",
                database);
            if (failure is not null)
                return failure;
        }

        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;
            var scope = $"Table '{table.DbName}'";

            foreach (var (limitType, amount) in table.CacheLimits)
            {
                var failure = ValidateCacheLimit(limitType, amount, scope, table);
                if (failure is not null)
                    return failure;
            }

            foreach (var (indexCacheType, amount) in table.IndexCache)
            {
                var failure = ValidateIndexCachePolicy(indexCacheType, amount, scope, table);
                if (failure is not null)
                    return failure;
            }
        }

        return true;
    }

    private static IDLOptionFailure? ValidateCacheLimit(
        CacheLimitType limitType,
        long amount,
        string scope,
        IDefinition context)
    {
        if (!Enum.IsDefined(typeof(CacheLimitType), limitType))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has cache limit metadata with unsupported cache limit type '{limitType}'.",
                context);

        if (amount <= 0)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has cache limit metadata for '{limitType}' with amount '{amount}'. Cache limit amounts must be greater than zero.",
                context);

        return null;
    }

    private static IDLOptionFailure? ValidateCacheCleanup(
        CacheCleanupType cleanupType,
        long amount,
        string scope,
        IDefinition context)
    {
        if (!Enum.IsDefined(typeof(CacheCleanupType), cleanupType))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has cache cleanup metadata with unsupported cache cleanup type '{cleanupType}'.",
                context);

        if (amount <= 0)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has cache cleanup metadata for '{cleanupType}' with amount '{amount}'. Cache cleanup amounts must be greater than zero.",
                context);

        return null;
    }

    private static IDLOptionFailure? ValidateIndexCachePolicy(
        IndexCacheType indexCacheType,
        int? amount,
        string scope,
        IDefinition context)
    {
        if (!Enum.IsDefined(typeof(IndexCacheType), indexCacheType))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has index-cache metadata with unsupported index-cache type '{indexCacheType}'.",
                context);

        if (indexCacheType == IndexCacheType.MaxAmountRows && !amount.HasValue)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has MaxAmountRows index-cache metadata without a row amount.",
                context);

        if (amount is <= 0)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has index-cache metadata for '{indexCacheType}' with amount '{amount}'. Index-cache amounts must be greater than zero.",
                context);

        return null;
    }

    private static SourceLocation? GetTableNameSourceLocation(ModelDefinition model)
    {
        var tableAttribute = model.Attributes
            .FirstOrDefault(x => x is TableAttribute or ViewAttribute);

        if (tableAttribute != null)
        {
            var attributeLocation = model.GetAttributeSourceLocation(tableAttribute);
            if (attributeLocation.HasValue)
                return attributeLocation;
        }

        return model.GetSourceLocation();
    }

    public static Option<bool, IDLOptionFailure> ValidateExistingTableModels(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels)
        {
            if (tableModel is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Database '{database.DbName}' contains a null table model.",
                    database);

            if (!ReferenceEquals(tableModel.Database, database))
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Table model '{tableModel.CsPropertyName}' is registered on database '{database.DbName}', but belongs to database '{tableModel.Database.DbName}'.",
                    database);

            if (tableModel.Table is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Table model '{tableModel.CsPropertyName}' on database '{database.DbName}' has no table definition.",
                    database);

            if (tableModel.Model is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Table model '{tableModel.CsPropertyName}' on database '{database.DbName}' has no model definition.",
                    database);

            if (!ReferenceEquals(tableModel.Table.TableModel, tableModel))
                return CreateTableFailure(
                    tableModel.Table,
                    $"Table '{tableModel.Table.DbName}' is registered through table model '{tableModel.CsPropertyName}', but the table points at a different table model.");

            if (!ReferenceEquals(tableModel.Model.TableModel, tableModel))
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Model '{tableModel.Model.CsType.Name}' is registered through table model '{tableModel.CsPropertyName}', but the model points at a different table model.",
                    tableModel.Model);

            if (string.IsNullOrWhiteSpace(tableModel.CsPropertyName))
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Table '{tableModel.Table.DbName}' has an empty database property name.",
                    tableModel.Table);
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateUniqueColumnNames(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var duplicateGroup = tableModel.Table.Columns
                .GroupBy(x => x.DbName, StringComparer.Ordinal)
                .FirstOrDefault(x => x.Count() > 1);

            if (duplicateGroup == null)
                continue;

            var duplicates = duplicateGroup.ToArray();
            var first = duplicates[0];
            var duplicate = duplicates[1];
            var message = $"Duplicate column definition for '{duplicateGroup.Key}' in table '{tableModel.Table.DbName}'. Properties '{first.ValueProperty.PropertyName}' and '{duplicate.ValueProperty.PropertyName}' both map to the same column name.";
            var sourceLocation = GetColumnNameSourceLocation(duplicate.ValueProperty);

            return sourceLocation.HasValue
                ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
                : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, duplicate);
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateExistingPrimaryKeyColumns(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;

            foreach (var primaryKeyColumn in table.PrimaryKeyColumns)
            {
                if (primaryKeyColumn is null)
                    return CreateTableFailure(
                        table,
                        $"Table '{table.DbName}' contains a null primary-key column.");

                if (!ReferenceEquals(primaryKeyColumn.Table, table))
                    return CreateTableFailure(
                        table,
                        $"Table '{table.DbName}' has primary-key column '{primaryKeyColumn.Table.DbName}.{primaryKeyColumn.DbName}', but primary-key columns must belong to the table.");

                if (!table.Columns.Contains(primaryKeyColumn))
                    return CreateColumnPropertyFailure(
                        primaryKeyColumn,
                        $"Table '{table.DbName}' has primary-key column '{primaryKeyColumn.DbName}', but that column is not registered on the table.");

                if (!primaryKeyColumn.PrimaryKey)
                    return CreateColumnPropertyFailure(
                        primaryKeyColumn,
                        $"Table '{table.DbName}' has primary-key column '{primaryKeyColumn.DbName}', but the column is not marked as a primary key.");
            }

            foreach (var column in table.Columns)
            {
                if (column is null)
                    continue;

                if (column.PrimaryKey && !table.PrimaryKeyColumns.Contains(column))
                    return CreateColumnPropertyFailure(
                        column,
                        $"Column '{table.DbName}.{column.DbName}' is marked as a primary key, but it is not registered in the table primary-key columns.");
            }
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateExistingColumnIndices(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;

            foreach (var index in table.ColumnIndices)
            {
                if (index is null)
                    return CreateColumnIndexFailure(
                        table,
                        $"Table '{table.DbName}' contains a null column index.");

                if (index.Columns.Count == 0)
                    return CreateColumnIndexFailure(
                        table,
                        $"Index '{index.Name}' on table '{table.DbName}' must include at least one column.");

                if (!ReferenceEquals(index.Table, table))
                {
                    var indexTableName = index.Table?.DbName ?? "<unknown>";
                    return CreateColumnIndexFailure(
                        table,
                        $"Index '{index.Name}' is attached to table '{table.DbName}', but the index belongs to table '{indexTableName}'. Column indices must be stored on the table that owns their columns.");
                }

                foreach (var column in index.Columns)
                {
                    if (column is null)
                        return CreateColumnIndexFailure(
                            table,
                            $"Index '{index.Name}' on table '{table.DbName}' contains a null column reference.");

                    if (!ReferenceEquals(column.Table, table))
                        return CreateColumnIndexFailure(
                            table,
                            $"Index '{index.Name}' on table '{table.DbName}' references column '{column.Table.DbName}.{column.DbName}', but index columns must belong to the table that stores the index.");

                    if (!table.Columns.Contains(column))
                        return CreateColumnIndexFailure(
                            table,
                            $"Index '{index.Name}' on table '{table.DbName}' references column '{column.DbName}', but that column is not registered on the table.");
                }
            }
        }

        return true;
    }

    private static IDLOptionFailure CreateColumnIndexFailure(TableDefinition table, string message) =>
        DLOptionFailure.Fail(DLFailureType.InvalidModel, message, table);

    public static Option<bool, IDLOptionFailure> ValidateExistingColumnPropertyBindings(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;
            var model = tableModel.Model;

            foreach (var column in table.Columns)
            {
                if (column is null)
                    return CreateTableFailure(table, $"Table '{table.DbName}' contains a null column.");

                if (!ReferenceEquals(column.Table, table))
                    return CreateTableFailure(
                        table,
                        $"Column '{column.DbName}' is registered on table '{table.DbName}', but the column belongs to table '{column.Table.DbName}'.");

                if (column.ValueProperty is not { } valueProperty)
                    return CreateColumnPropertyFailure(
                        column,
                        $"Column '{table.DbName}.{column.DbName}' has no value property.");

                if (!ReferenceEquals(valueProperty.Model, model))
                    return CreateColumnPropertyFailure(
                        column,
                        $"Column '{table.DbName}.{column.DbName}' is linked to value property '{GetValuePropertyDisplayName(valueProperty)}', but that property belongs to model '{valueProperty.Model.CsType.Name}' instead of '{model.CsType.Name}'.");

                if (!ReferenceEquals(valueProperty.Column, column))
                {
                    var propertyColumnName = valueProperty.Column is null
                        ? "<none>"
                        : $"{valueProperty.Column.Table.DbName}.{valueProperty.Column.DbName}";
                    return CreateColumnPropertyFailure(
                        column,
                        $"Column '{table.DbName}.{column.DbName}' is linked to value property '{GetValuePropertyDisplayName(valueProperty)}', but that property points at column '{propertyColumnName}'.");
                }

                if (!model.ValueProperties.TryGetValue(valueProperty.PropertyName, out var registeredProperty) ||
                    !ReferenceEquals(registeredProperty, valueProperty))
                {
                    return CreateColumnPropertyFailure(
                        column,
                        $"Column '{table.DbName}.{column.DbName}' is linked to value property '{GetValuePropertyDisplayName(valueProperty)}', but that property is not registered on model '{model.CsType.Name}'.");
                }
            }

            foreach (var entry in model.ValueProperties)
            {
                var propertyName = entry.Key;
                var property = entry.Value;

                if (property is null)
                    return CreateTableFailure(
                        table,
                        $"Model '{model.CsType.Name}' contains a null value property for key '{propertyName}'.");

                if (!string.Equals(propertyName, property.PropertyName, StringComparison.Ordinal))
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' is registered under key '{propertyName}' instead of its own property name.");

                if (!ReferenceEquals(property.Model, model))
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' is registered on model '{model.CsType.Name}', but belongs to model '{property.Model.CsType.Name}'.");

                if (property.Column is not { } column)
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' has no column.");

                if (!ReferenceEquals(column.Table, table))
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' references column '{column.Table.DbName}.{column.DbName}', but the property belongs to table '{table.DbName}'.");

                if (!table.Columns.Contains(column))
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' references column '{table.DbName}.{column.DbName}', but that column is not registered on the table.");

                if (!ReferenceEquals(column.ValueProperty, property))
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' references column '{table.DbName}.{column.DbName}', but that column points at a different value property.");
            }
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateViewDefinitions(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            if (tableModel.Table is not ViewDefinition { Definition: null } view)
                continue;

            return CreateTableFailure(
                view,
                $"View '{view.DbName}' on model '{tableModel.Model.CsType.Name}' is missing a SQL definition. Add a DefinitionAttribute to the view model or set the provider view definition before building metadata.");
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateExistingColumnTypes(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;

            foreach (var column in table.Columns)
            {
                foreach (var dbType in column.DbTypes)
                {
                    if (dbType is null)
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' contains a null database type.");

                    if (!Enum.IsDefined(typeof(DatabaseType), dbType.DatabaseType))
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' has database type '{dbType.Name}' with unsupported database type '{dbType.DatabaseType}'.");

                    if (string.IsNullOrWhiteSpace(dbType.Name))
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' has an empty database type name for database type '{dbType.DatabaseType}'.");
                }
            }
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateValuePropertyEnums(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            foreach (var property in tableModel.Model.ValueProperties.Values)
            {
                if (!property.EnumProperty.HasValue)
                {
                    if (property.CsType.Type?.IsEnum == true)
                        return CreateValuePropertyFailure(
                            property,
                            $"Value property '{GetValuePropertyDisplayName(property)}' has enum C# type '{property.CsType.Name}', but no enum metadata is attached.");

                    continue;
                }

                var enumProperty = property.EnumProperty.Value;
                var csValues = enumProperty.CsEnumValues ?? [];
                var explicitDbValues = enumProperty.DbEnumValues ?? [];
                var dbValues = explicitDbValues.Count != 0 ? explicitDbValues : csValues;

                if (!IsValidCSharpIdentifier(property.CsType.Name))
                    return CreateValuePropertyFailure(
                        property,
                        $"Enum value property '{GetValuePropertyDisplayName(property)}' uses C# enum type name '{property.CsType.Name}', which is not a valid unescaped C# identifier.");

                if (csValues.Count == 0 && explicitDbValues.Count == 0)
                    return CreateValuePropertyFailure(
                        property,
                        $"Enum value property '{GetValuePropertyDisplayName(property)}' must define at least one enum value.");

                foreach (var (name, _) in csValues)
                {
                    if (!IsValidCSharpIdentifier(name))
                        return CreateValuePropertyFailure(
                            property,
                            $"Enum value property '{GetValuePropertyDisplayName(property)}' has invalid C# enum member name '{name}'. Enum member names must be valid unescaped C# identifiers.");
                }

                var duplicateCsValue = csValues
                    .GroupBy(x => x.name, StringComparer.Ordinal)
                    .FirstOrDefault(x => x.Count() > 1);
                if (duplicateCsValue != null)
                    return CreateValuePropertyFailure(
                        property,
                        $"Enum value property '{GetValuePropertyDisplayName(property)}' defines duplicate C# enum member name '{duplicateCsValue.Key}'.");

                foreach (var (name, _) in dbValues)
                {
                    if (name is null)
                        return CreateValuePropertyFailure(
                            property,
                            $"Enum value property '{GetValuePropertyDisplayName(property)}' has a null database enum value.");
                }

                var duplicateDbValue = dbValues
                    .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(x => x.Count() > 1);
                if (duplicateDbValue != null)
                    return CreateValuePropertyFailure(
                        property,
                        $"Enum value property '{GetValuePropertyDisplayName(property)}' defines duplicate database enum value '{duplicateDbValue.Key}'. Database enum values must be unique ignoring case for runtime value mapping.");
            }
        }

        return true;
    }

    private static bool IsValidCSharpIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var identifier = value!;

        if (!IsCSharpIdentifierStart(identifier[0]))
            return false;

        if (CSharpKeywords.Contains(identifier))
            return false;

        for (var i = 1; i < identifier.Length; i++)
        {
            if (!IsCSharpIdentifierPart(identifier[i]))
                return false;
        }

        return true;
    }

    private static bool IsValidCSharpTypeReferenceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var typeName = value!;
        if (!string.Equals(typeName, typeName.Trim(), StringComparison.Ordinal))
            return false;

        var typeSyntax = SyntaxFactory.ParseTypeName(typeName);
        return typeSyntax.FullSpan.Length == typeName.Length &&
            !typeSyntax.GetDiagnostics().Any();
    }

    private static bool IsCSharpIdentifierStart(char character) =>
        character == '_' || char.IsLetter(character);

    private static bool IsCSharpIdentifierPart(char character) =>
        character == '_' || char.IsLetterOrDigit(character);

    private static string GetValuePropertyDisplayName(ValueProperty property) =>
        $"{property.Model.CsType.Name}.{property.PropertyName}";

    private static IDLOptionFailure CreateTableFailure(TableDefinition table, string message) =>
        DLOptionFailure.Fail(DLFailureType.InvalidModel, message, table);

    private static IDLOptionFailure CreateColumnPropertyFailure(ColumnDefinition column, string message) =>
        DLOptionFailure.Fail(DLFailureType.InvalidModel, message, column);

    private static IDLOptionFailure CreateValuePropertyFailure(ValueProperty property, string message)
    {
        if (property.SourceInfo.HasValue && property.CsFile.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value));

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, property);
    }

    public static Option<bool, IDLOptionFailure> ValidateValuePropertyDefaults(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            foreach (var property in tableModel.Model.ValueProperties.Values)
            {
                var defaultAttributes = property.Attributes
                    .OfType<DefaultAttribute>()
                    .ToArray();

                if (defaultAttributes.Length == 0)
                    continue;

                if (defaultAttributes.Length > 1)
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' has multiple default attributes. A value property can define only one default value.");

                var defaultAttribute = defaultAttributes[0];

                if (defaultAttribute is DefaultSqlAttribute defaultSql &&
                    !Enum.IsDefined(typeof(DatabaseType), defaultSql.DatabaseType))
                {
                    return CreateValuePropertyFailure(
                        property,
                        $"Default SQL attribute on value property '{GetValuePropertyDisplayName(property)}' uses unsupported database type '{defaultSql.DatabaseType}'.");
                }

                if (defaultAttribute is DefaultCurrentTimestampAttribute &&
                    !CanUseCurrentTimestampDefault(property.CsType.Name))
                {
                    return CreateValuePropertyFailure(
                        property,
                        $"DefaultCurrentTimestampAttribute can only be used with DateOnly, TimeOnly, DateTime, or DateTimeOffset properties, but value property '{GetValuePropertyDisplayName(property)}' has C# type '{property.CsType.Name}'.");
                }

                if (defaultAttribute is DefaultNewUUIDAttribute defaultNewUuid)
                {
                    if (!IsGuidType(property.CsType.Name))
                        return CreateValuePropertyFailure(
                            property,
                            $"DefaultNewUUIDAttribute can only be used with Guid properties, but value property '{GetValuePropertyDisplayName(property)}' has C# type '{property.CsType.Name}'.");

                    if (!Enum.IsDefined(typeof(UUIDVersion), defaultNewUuid.Version))
                        return CreateValuePropertyFailure(
                            property,
                            $"DefaultNewUUIDAttribute on value property '{GetValuePropertyDisplayName(property)}' uses unsupported UUID version '{defaultNewUuid.Version}'.");
                }

                if (defaultAttribute is not DefaultSqlAttribute)
                {
                    try
                    {
                        property.GetDefaultValueCode();
                    }
                    catch (Exception exception)
                    {
                        return CreateValuePropertyFailure(
                            property,
                            $"Default value for value property '{GetValuePropertyDisplayName(property)}' is not compatible with C# type '{property.CsType.Name}': {exception.Message}");
                    }
                }
            }
        }

        return true;
    }

    private static bool CanUseCurrentTimestampDefault(string csTypeName) =>
        csTypeName is "DateOnly" or "TimeOnly" or "DateTime" or "DateTimeOffset";

    private static bool IsGuidType(string csTypeName) =>
        csTypeName is "Guid" or "System.Guid";

    public static Option<bool, IDLOptionFailure> ValidateExistingRelationPropertyBindings(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;
            var model = tableModel.Model;

            foreach (var entry in model.RelationProperties)
            {
                var propertyName = entry.Key;
                var property = entry.Value;

                if (property is null)
                    return CreateTableFailure(
                        table,
                        $"Model '{model.CsType.Name}' contains a null relation property for key '{propertyName}'.");

                if (!string.Equals(propertyName, property.PropertyName, StringComparison.Ordinal))
                    return CreateRelationPropertyFailure(
                        property,
                        null,
                        $"Relation property '{GetRelationPropertyDisplayName(property)}' is registered under key '{propertyName}' instead of its own property name.");

                if (!ReferenceEquals(property.Model, model))
                    return CreateRelationPropertyFailure(
                        property,
                        null,
                        $"Relation property '{GetRelationPropertyDisplayName(property)}' is registered on model '{model.CsType.Name}', but belongs to model '{property.Model.CsType.Name}'.");

                if (model.ValueProperties.ContainsKey(property.PropertyName))
                    return CreateRelationPropertyFailure(
                        property,
                        null,
                        $"Model '{model.CsType.Name}' contains both a value property and a relation property named '{property.PropertyName}'. Property names must be unique within a model.");

                if (property.RelationPart?.ColumnIndex is { } relationIndex &&
                    !ReferenceEquals(relationIndex.Table, table))
                {
                    return CreateRelationPropertyFailure(
                        property,
                        null,
                        $"Relation property '{model.CsType.Name}.{property.PropertyName}' is linked to relation part on table '{relationIndex.Table.DbName}', but relation properties must point at a relation part on their own table '{table.DbName}'.");
                }
            }
        }

        return true;
    }

    private static string GetRelationPropertyDisplayName(RelationProperty property) =>
        $"{property.Model.CsType.Name}.{property.PropertyName}";

    public static Option<bool, IDLOptionFailure> ValidateExistingRelationParts(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            foreach (var relationProperty in tableModel.Model.RelationProperties.Values)
            {
                if (relationProperty.RelationPart is not { } relationPart)
                    continue;

                var failure = ValidateExistingRelationPart(
                    relationPart,
                    $"relation property '{tableModel.Model.CsType.Name}.{relationProperty.PropertyName}'",
                    message => CreateRelationPropertyFailure(relationProperty, null, message));
                if (failure != null)
                    return failure;
            }

            foreach (var index in tableModel.Table.ColumnIndices)
            {
                if (index is null)
                    continue;

                foreach (var relationPart in index.RelationParts)
                {
                    if (relationPart is null)
                        return CreateColumnIndexFailure(
                            tableModel.Table,
                            $"Index '{index.Name}' on table '{tableModel.Table.DbName}' contains a null relation part.");

                    if (!ReferenceEquals(relationPart.ColumnIndex, index))
                    {
                        var relationPartIndexName = relationPart.ColumnIndex?.Name ?? "<unknown>";
                        return CreateColumnIndexFailure(
                            tableModel.Table,
                            $"Index '{index.Name}' on table '{tableModel.Table.DbName}' contains relation part for index '{relationPartIndexName}'. Relation parts must be registered on their own column index.");
                    }

                    var failure = ValidateExistingRelationPart(
                        relationPart,
                        $"index '{index.Name}' on table '{tableModel.Table.DbName}'",
                        message => CreateColumnIndexFailure(tableModel.Table, message));
                    if (failure != null)
                        return failure;
                }
            }
        }

        return true;
    }

    private static IDLOptionFailure? ValidateExistingRelationPart(
        RelationPart relationPart,
        string ownerDescription,
        Func<string, IDLOptionFailure> createFailure)
    {
        if (relationPart.ColumnIndex is null)
            return createFailure($"Existing relation part referenced by {ownerDescription} has no column index.");

        if (relationPart.Relation is null)
            return createFailure($"Existing relation part referenced by {ownerDescription} has no relation definition.");

        var relation = relationPart.Relation;
        var relationName = string.IsNullOrWhiteSpace(relation.ConstraintName)
            ? "<unnamed>"
            : relation.ConstraintName;

        if (relation.ForeignKey is null)
            return createFailure($"Existing relation '{relationName}' referenced by {ownerDescription} is missing a foreign-key part.");

        if (relation.CandidateKey is null)
            return createFailure($"Existing relation '{relationName}' referenced by {ownerDescription} is missing a candidate-key part.");

        if (!ReferenceEquals(relation.ForeignKey.Relation, relation))
            return createFailure($"Existing relation '{relationName}' has a foreign-key part that points at a different relation definition.");

        if (!ReferenceEquals(relation.CandidateKey.Relation, relation))
            return createFailure($"Existing relation '{relationName}' has a candidate-key part that points at a different relation definition.");

        if (!ReferenceEquals(relationPart, relation.ForeignKey) &&
            !ReferenceEquals(relationPart, relation.CandidateKey))
        {
            return createFailure($"Existing relation part referenced by {ownerDescription} is not one of relation '{relationName}'s registered sides.");
        }

        if (relation.ForeignKey.Type != RelationPartType.ForeignKey)
            return createFailure($"Existing relation '{relationName}' has a foreign-key part marked as '{relation.ForeignKey.Type}'.");

        if (relation.CandidateKey.Type != RelationPartType.CandidateKey)
            return createFailure($"Existing relation '{relationName}' has a candidate-key part marked as '{relation.CandidateKey.Type}'.");

        if (relation.ForeignKey.ColumnIndex is null)
            return createFailure($"Existing relation '{relationName}' has a foreign-key part without a column index.");

        if (relation.CandidateKey.ColumnIndex is null)
            return createFailure($"Existing relation '{relationName}' has a candidate-key part without a column index.");

        if (!relation.ForeignKey.ColumnIndex.RelationParts.Contains(relation.ForeignKey))
            return createFailure($"Existing relation '{relationName}' foreign-key part is not registered on index '{relation.ForeignKey.ColumnIndex.Name}'.");

        if (!relation.CandidateKey.ColumnIndex.RelationParts.Contains(relation.CandidateKey))
            return createFailure($"Existing relation '{relationName}' candidate-key part is not registered on index '{relation.CandidateKey.ColumnIndex.Name}'.");

        if (relation.ForeignKey.ColumnIndex.Columns.Count != relation.CandidateKey.ColumnIndex.Columns.Count)
        {
            return createFailure(
                $"Existing relation '{relationName}' has mismatched column counts between foreign-key index '{relation.ForeignKey.ColumnIndex.Name}' and candidate-key index '{relation.CandidateKey.ColumnIndex.Name}'.");
        }

        return null;
    }

    private static SourceLocation? GetColumnNameSourceLocation(ValueProperty property)
    {
        var columnAttribute = property.Attributes
            .FirstOrDefault(x => x is ColumnAttribute);

        if (columnAttribute != null)
        {
            var attributeLocation = property.GetAttributeSourceLocation(columnAttribute);
            if (attributeLocation.HasValue)
                return attributeLocation;
        }

        if (property.SourceInfo.HasValue && property.CsFile.HasValue)
            return property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value);

        return null;
    }

    public static Option<bool, IDLOptionFailure> ParseRelations(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Where(x => !x.IsStub && x.Table.Type == TableType.Table).Select(x => x.Table))
        {
            var columns = table.Columns.Where(x => x.PrimaryKey).ToList();
            if (!columns.Any())
                return CreateMissingPrimaryKeyFailure(table);

            if (!table.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey))
                table.ColumnIndices.Add(new ColumnIndex($"{table.DbName}_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, columns));
        }

        foreach (var foreignKeyGroup in database.TableModels
            .Where(x => !x.IsStub && x.Table.Type == TableType.Table)
            .Select(x => x.Table)
            .SelectMany(table => table.Columns
                .Where(column => column.ForeignKey)
                .SelectMany(column => column.ValueProperty.Attributes
                    .OfType<ForeignKeyAttribute>()
                    .Select(attribute => new ForeignKeyColumn(column, attribute))))
            .GroupBy(x => new { ForeignKeyTable = x.Column.Table, x.Attribute.Name, CandidateTableName = x.Attribute.Table }))
        {
            var orderedForeignKeys = foreignKeyGroup
                .OrderBy(x => x.Attribute.Ordinal ?? x.Column.Index)
                .ThenBy(x => x.Column.Index)
                .ToList();
            var firstForeignKey = orderedForeignKeys[0];
            var firstAttribute = firstForeignKey.Attribute;
            var foreignKeyTable = firstForeignKey.Column.Table;
            var candidateTableModel = database
                .TableModels.FirstOrDefault(x => x.Table.DbName == firstAttribute.Table);

            if (candidateTableModel == null)
                return CreateForeignKeyFailure(
                    firstForeignKey.Column,
                    firstAttribute,
                    $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' references table '{firstAttribute.Table}', but no matching table exists in database '{database.DbName}'.");

            var candidateColumns = new List<ColumnDefinition>();
            foreach (var foreignKey in orderedForeignKeys)
            {
                var foreignKeyColumn = foreignKey.Column;
                var attribute = foreignKey.Attribute;
                var candidateColumn = candidateTableModel
                    .Table.Columns.FirstOrDefault(x => x.DbName == attribute.Column);

                if (candidateColumn == null)
                    return CreateForeignKeyFailure(
                        foreignKeyColumn,
                        attribute,
                        $"Foreign key '{attribute.Name}' on column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' references column '{attribute.Table}.{attribute.Column}', but that column does not exist.");

                candidateColumns.Add(candidateColumn);
            }

            var foreignKeyColumns = orderedForeignKeys.Select(x => x.Column).ToList();
            var manySideModel = foreignKeyTable.Model;
            var oneSideModel = candidateTableModel.Model;

            var foreignKeyIndex = foreignKeyTable.ColumnIndices.FirstOrDefault(x =>
                x.Characteristic == IndexCharacteristic.ForeignKey &&
                x.Name == firstAttribute.Name &&
                ColumnsMatch(x.Columns, foreignKeyColumns));
            if (foreignKeyIndex == null)
            {
                if (!TryCreateColumnIndex(
                    firstAttribute.Name,
                    IndexCharacteristic.ForeignKey,
                    IndexType.BTREE,
                    foreignKeyColumns,
                    out foreignKeyIndex,
                    out var foreignKeyIndexFailure))
                {
                    return CreateForeignKeyFailure(
                        firstForeignKey.Column,
                        firstAttribute,
                        $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' could not create its index: {foreignKeyIndexFailure}");
                }

                foreignKeyTable.ColumnIndices.Add(foreignKeyIndex);
            }

            var candidateKeyIndex = FindCandidateKeyIndex(candidateTableModel.Table, candidateColumns);
            if (candidateKeyIndex == null)
                return CreateForeignKeyFailure(
                    firstForeignKey.Column,
                    firstAttribute,
                    $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' references columns '{candidateColumns.Select(x => x.DbName).ToJoinedString(", ")}' on table '{candidateTableModel.Table.DbName}', but no matching primary or unique key exists.");

            var relation = new RelationDefinition(firstAttribute.Name, RelationType.OneToMany)
            {
                OnUpdate = firstAttribute.OnUpdate,
                OnDelete = firstAttribute.OnDelete
            };
            var manySidePart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "");
            var oneSidePart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, "");
            relation.ForeignKey = manySidePart;
            relation.CandidateKey = oneSidePart;

            // --- Link or Create Many-to-One Property ---
            if (!TryGetRelationProperty(
                manySideModel,
                oneSideModel.Table.DbName,
                candidateColumns.Select(x => x.DbName).ToArray(),
                firstAttribute.Name,
                out var manyToOneProp,
                out var manyToOnePropertyFailure))
            {
                return manyToOnePropertyFailure;
            }

            if (manyToOneProp != null)
            {
                manyToOneProp.SetRelationPart(manySidePart);
                if (!manySidePart.ColumnIndex.RelationParts.Contains(manySidePart))
                    manySidePart.ColumnIndex.RelationParts.Add(manySidePart);
            }
            else
            {
                var propName = GetForeignKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute);
                var propType = oneSideModel.CsType;
                var propAttr = new RelationAttribute(oneSideModel.Table.DbName, candidateColumns.Select(x => x.DbName).ToArray(), firstAttribute.Name);
                AddRelationProperty(manySideModel, propName, propType, manySidePart, propAttr);
            }

            // --- Link or Create One-to-Many Property ---
            if (!TryGetRelationProperty(
                oneSideModel,
                manySideModel.Table.DbName,
                foreignKeyColumns.Select(x => x.DbName).ToArray(),
                firstAttribute.Name,
                out var oneToManyProp,
                out var oneToManyPropertyFailure))
            {
                return oneToManyPropertyFailure;
            }

            if (oneToManyProp != null)
            {
                oneToManyProp.SetRelationPart(oneSidePart);
                if (!oneSidePart.ColumnIndex.RelationParts.Contains(oneSidePart))
                    oneSidePart.ColumnIndex.RelationParts.Add(oneSidePart);
            }
            else
            {
                var propName = GetCandidateKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute);
                var genericTypeName = manySideModel.CsType.Name;
                var propType = new CsTypeDeclaration($"IImmutableRelation<{genericTypeName}>", "DataLinq.Instances", ModelCsType.Interface);
                var propAttr = new RelationAttribute(manySideModel.Table.DbName, foreignKeyColumns.Select(x => x.DbName).ToArray(), firstAttribute.Name);
                AddRelationProperty(oneSideModel, propName, propType, oneSidePart, propAttr);
            }
        }

        return ValidateResolvedRelationProperties(database);
    }

    public static Option<bool, IDLOptionFailure> ValidateResolvedRelationProperties(DatabaseDefinition database)
    {
        var unresolvedRelation = database.TableModels
            .Where(x => !x.IsStub)
            .SelectMany(x => x.Model.RelationProperties.Values)
            .FirstOrDefault(x => x.RelationPart == null && ShouldValidateUnresolvedRelation(database, x));

        if (unresolvedRelation == null)
            return true;

        var relationAttribute = unresolvedRelation.Attributes
            .OfType<RelationAttribute>()
            .FirstOrDefault();
        var target = relationAttribute == null
            ? "a matching foreign-key relation"
            : $"relation target '{relationAttribute.Table}.({relationAttribute.Columns.ToJoinedString(", ")})'";
        var message = $"Relation property '{unresolvedRelation.Model.CsType.Name}.{unresolvedRelation.PropertyName}' could not be resolved to {target}. Check that the [Relation] table, column, and constraint name match a [ForeignKey] definition.";
        var sourceLocation = relationAttribute == null
            ? null
            : unresolvedRelation.GetAttributeSourceLocation(relationAttribute);

        if (!sourceLocation.HasValue && unresolvedRelation.SourceInfo.HasValue && unresolvedRelation.CsFile.HasValue)
            sourceLocation = unresolvedRelation.SourceInfo.Value.GetPropertyLocation(unresolvedRelation.CsFile.Value);

        return sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, unresolvedRelation);
    }

    private static bool ShouldValidateUnresolvedRelation(DatabaseDefinition database, RelationProperty relation)
    {
        var relationAttribute = relation.Attributes
            .OfType<RelationAttribute>()
            .FirstOrDefault();

        if (relationAttribute == null)
            return true;

        return database.TableModels
            .Where(x => !x.IsStub)
            .Any(x => x.Table.DbName == relationAttribute.Table);
    }

    private static IDLOptionFailure CreateMissingPrimaryKeyFailure(TableDefinition table)
    {
        var message = $"Table '{table.DbName}' is missing a primary key.";
        var sourceLocation = GetTableNameSourceLocation(table.Model);

        return sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, table);
    }

    private static IDLOptionFailure CreateForeignKeyFailure(ColumnDefinition foreignKeyColumn, ForeignKeyAttribute attribute, string message)
    {
        var attributeLocation = foreignKeyColumn.ValueProperty.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var property = foreignKeyColumn.ValueProperty;
        if (property.SourceInfo.HasValue && property.CsFile.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value));

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, foreignKeyColumn);
    }

    private static ColumnIndex? FindCandidateKeyIndex(TableDefinition table, IReadOnlyList<ColumnDefinition> columns)
    {
        return table.ColumnIndices.FirstOrDefault(index =>
            index.Characteristic is IndexCharacteristic.PrimaryKey or IndexCharacteristic.Unique &&
            ColumnsMatch(index.Columns, columns));
    }

    private static bool ColumnsMatch(IReadOnlyList<ColumnDefinition> left, IReadOnlyList<ColumnDefinition> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static bool TryCreateColumnIndex(
        string name,
        IndexCharacteristic characteristic,
        IndexType type,
        List<ColumnDefinition> columns,
        out ColumnIndex index,
        out string failure)
    {
        try
        {
            index = new ColumnIndex(name, characteristic, type, columns);
            failure = null!;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            index = null!;
            failure = exception.Message;
            return false;
        }
        catch (ArgumentException exception)
        {
            index = null!;
            failure = exception.Message;
            return false;
        }
    }

    private sealed class ForeignKeyColumn
    {
        public ForeignKeyColumn(ColumnDefinition column, ForeignKeyAttribute attribute)
        {
            Column = column;
            Attribute = attribute;
        }

        public ColumnDefinition Column { get; }
        public ForeignKeyAttribute Attribute { get; }

        public void Deconstruct(out ColumnDefinition column, out ForeignKeyAttribute attribute)
        {
            column = Column;
            attribute = Attribute;
        }
    }

    private static bool TryGetRelationProperty(
        ModelDefinition model,
        string referencedTableName,
        string[] referencedColumnNames,
        string constraintName,
        out RelationProperty? property,
        out IDLOptionFailure failure)
    {
        // Find a property in the model that has a [Relation] attribute matching the target table, columns, and constraint name.
        var matches = model.RelationProperties.Values
            .Where(p => p.Attributes.OfType<RelationAttribute>().Any(a =>
                RelationAttributeMatches(a, referencedTableName, referencedColumnNames, constraintName)))
            .ToArray();

        if (matches.Length <= 1)
        {
            property = matches.FirstOrDefault();
            failure = null!;
            return true;
        }

        property = null;

        var target = $"{referencedTableName}.({referencedColumnNames.ToJoinedString(", ")})";
        var propertyNames = matches.Select(x => x.PropertyName).ToJoinedString(", ");
        var message = $"Multiple relation properties on model '{model.CsType.Name}' match relation target '{target}' for constraint '{constraintName}': {propertyNames}. Relation attributes must identify at most one property for a database relation.";
        var duplicateAttribute = matches[1]
            .Attributes
            .OfType<RelationAttribute>()
            .FirstOrDefault(attribute => RelationAttributeMatches(attribute, referencedTableName, referencedColumnNames, constraintName));

        failure = CreateRelationPropertyFailure(matches[1], duplicateAttribute, message);
        return false;
    }

    private static bool RelationAttributeMatches(
        RelationAttribute attribute,
        string referencedTableName,
        string[] referencedColumnNames,
        string constraintName) =>
        attribute.Table == referencedTableName &&
        attribute.Columns.SequenceEqual(referencedColumnNames) &&
        (attribute.Name == null || attribute.Name == constraintName);

    private static IDLOptionFailure CreateRelationPropertyFailure(
        RelationProperty relation,
        RelationAttribute? attribute,
        string message)
    {
        if (attribute != null)
        {
            var attributeLocation = relation.GetAttributeSourceLocation(attribute);
            if (attributeLocation.HasValue)
                return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);
        }

        if (relation.SourceInfo.HasValue && relation.CsFile.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, relation.SourceInfo.Value.GetPropertyLocation(relation.CsFile.Value));

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, relation);
    }

    private static string GetForeignKeyRelationPropertyName(ModelDefinition manySideModel, ModelDefinition oneSideModel, IReadOnlyList<ColumnDefinition> foreignKeyColumns, ForeignKeyAttribute attribute)
    {
        if (foreignKeyColumns.Count > 1)
        {
            if (!HasMultipleForeignKeyConstraintsBetween(manySideModel.Table, oneSideModel.Table))
                return oneSideModel.CsType.Name;

            var constraintName = GetRelationPropertyNameFromConstraint(oneSideModel.CsType.Name, attribute.Name);
            return string.IsNullOrEmpty(constraintName)
                ? oneSideModel.CsType.Name
                : constraintName;
        }

        return Regex.Replace(foreignKeyColumns[0].DbName, "(_id|id|fk)$", "", RegexOptions.IgnoreCase).ToPascalCase();
    }

    private static string GetCandidateKeyRelationPropertyName(ModelDefinition manySideModel, ModelDefinition oneSideModel, IReadOnlyList<ColumnDefinition> foreignKeyColumns, ForeignKeyAttribute attribute)
    {
        if (!HasMultipleForeignKeyConstraintsBetween(manySideModel.Table, oneSideModel.Table))
            return manySideModel.CsType.Name;

        var relationName = attribute.Name.Any(char.IsLetter)
            ? GetRelationPropertyNameFromConstraint(manySideModel.CsType.Name, attribute.Name)
            : GetRelationPropertyNameFromColumn(manySideModel.CsType.Name, foreignKeyColumns[0].DbName);

        return string.IsNullOrEmpty(relationName)
            ? manySideModel.CsType.Name
            : relationName;
    }

    private static bool HasMultipleForeignKeyConstraintsBetween(TableDefinition foreignKeyTable, TableDefinition candidateTable)
    {
        return foreignKeyTable.Columns
            .SelectMany(column => column.ValueProperty.Attributes.OfType<ForeignKeyAttribute>())
            .Where(attribute => attribute.Table == candidateTable.DbName)
            .Select(attribute => attribute.Name)
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;
    }

    private static string GetRelationPropertyNameFromConstraint(string fallbackPrefix, string constraintName)
    {
        var words = Regex
            .Split(constraintName, "[^A-Za-z0-9]+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToList();

        if (words.Count == 0)
            return string.Empty;

        if (string.Equals(words[0], "fk", StringComparison.OrdinalIgnoreCase))
            words.RemoveAt(0);

        if (words.Count > 0 && string.Equals(words[words.Count - 1], "fk", StringComparison.OrdinalIgnoreCase))
            words.RemoveAt(words.Count - 1);

        if (words.Count == 0)
            return string.Empty;

        var propertyName = words
            .Select(word => word.FirstCharToUpper())
            .ToJoinedString("");

        return propertyName.StartsWith(fallbackPrefix, StringComparison.Ordinal)
            ? propertyName
            : fallbackPrefix + propertyName;
    }

    private static string GetRelationPropertyNameFromColumn(string fallbackPrefix, string columnName)
    {
        var nameWithoutCommonSuffix = Regex.Replace(columnName, "(_id| id|id|fk)$", "", RegexOptions.IgnoreCase);
        var words = Regex
            .Split(nameWithoutCommonSuffix, "[^A-Za-z0-9]+")
            .Where(word => !string.IsNullOrWhiteSpace(word));

        var propertyName = words
            .Select(word => word.FirstCharToUpper())
            .ToJoinedString("");

        return string.IsNullOrEmpty(propertyName)
            ? string.Empty
            : fallbackPrefix + propertyName;
    }

    public static void AddRelationProperty(ModelDefinition model, string propertyName, CsTypeDeclaration propertyType, RelationPart relationPart, RelationAttribute relationAttribute)
    {
        var originalPropertyName = propertyName;
        var i = 2;
        while (model.RelationProperties.ContainsKey(propertyName) || model.ValueProperties.ContainsKey(propertyName))
        {
            propertyName = $"{originalPropertyName}_{i++}";
        }

        var relationProperty = new RelationProperty(propertyName, propertyType, model, [relationAttribute]);
        relationProperty.SetRelationName(relationAttribute.Name);
        relationProperty.SetRelationPart(relationPart); // Directly link the part
        if (relationPart.Type == RelationPartType.ForeignKey && relationPart.ColumnIndex.Columns.Any(x => x.Nullable))
            relationProperty.SetCsNullable();

        model.AddProperty(relationProperty);

        // Also ensure the back-reference on the index is set
        if (!relationPart.ColumnIndex.RelationParts.Contains(relationPart))
        {
            relationPart.ColumnIndex.RelationParts.Add(relationPart);
        }
    }

    public static ValueProperty AttachValueProperty(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        if (!TryAttachValueProperty(column, csTypeName, capitaliseNames).TryUnwrap(out var property, out var failure))
            throw new InvalidOperationException(failure.ToString());

        return property;
    }

    public static Option<ValueProperty, IDLOptionFailure> TryAttachValueProperty(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        var name = column.DbName.ToCSharpIdentifier(capitaliseNames);

        var type = MetadataTypeConverter.GetType(csTypeName);

        CsTypeDeclaration csType;

        if (type == null)
        {
            if (csTypeName == "enum")
                csType = new CsTypeDeclaration(csTypeName, "", ModelCsType.Enum);
            else
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Unsupported C# type '{csTypeName}' for column '{column.Table.DbName}.{column.DbName}'.",
                    column);
        }
        else
        {
            csType = new CsTypeDeclaration(type);
        }


        var property = new ValueProperty(name, csType, column.Table.Model, GetAttributes(column));
        property.SetCsSize(MetadataTypeConverter.CsTypeSize(csTypeName));
        property.SetCsNullable(column.Nullable || column.AutoIncrement);
        //property.SetAttributes(GetAttributes(property));
        property.SetColumn(column);

        column.SetValueProperty(property);
        column.Table.Model.AddProperty(column.ValueProperty);

        return property;
    }

    //public static void AttachEnumProperty(ValueProperty property, IEnumerable<(string name, int value)> enumValues, bool declaredInClass)
    //{
    //    property.SetEnumProperty(new EnumProperty(enumValues, enumValues, declaredInClass));
    //}

    public static IEnumerable<Attribute> GetAttributes(ColumnDefinition column)
    {
        if (column.PrimaryKey)
            yield return new PrimaryKeyAttribute();

        if (column.AutoIncrement)
            yield return new AutoIncrementAttribute();

        if (column.Nullable)
            yield return new NullableAttribute();

        yield return new ColumnAttribute(column.DbName);

        foreach (var dbType in column.DbTypes)
        {
            yield return new TypeAttribute(dbType);
        }
    }

    public static void ParseAttributes(this DatabaseDefinition database)
    {
        foreach (var attribute in database.Attributes)
        {
            if (attribute is DatabaseAttribute databaseAttribute)
            {
                database.SetName(databaseAttribute.Name);
                database.SetDbName(databaseAttribute.Name);
            }

            if (attribute is UseCacheAttribute useCache)
                database.SetCache(useCache.UseCache);

            if (attribute is CacheLimitAttribute cacheLimit)
                database.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                database.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (attribute is CacheCleanupAttribute cacheCleanup)
                database.CacheCleanup.Add((cacheCleanup.LimitType, cacheCleanup.Amount));
        }
    }

    public static ColumnDefinition ParseColumn(this TableDefinition table, ValueProperty property)
    {
        var column = new ColumnDefinition(property.PropertyName, table);
        column.SetValueProperty(property);

        foreach (var attribute in property.Attributes)
        {
            if (attribute is ColumnAttribute columnAttribute)
                column.SetDbName(columnAttribute.Name);

            if (attribute is NullableAttribute)
                column.SetNullable();

            //if (attribute is DefaultAttribute defaultAttribute)
            //    column.AddDefaultValue(defaultAttribute.Value);

            if (attribute is AutoIncrementAttribute)
                column.SetAutoIncrement();

            if (attribute is PrimaryKeyAttribute)
                column.SetPrimaryKey();

            if (attribute is ForeignKeyAttribute)
                column.SetForeignKey();

            if (attribute is TypeAttribute t)
                column.AddDbType(new DatabaseColumnType(t.DatabaseType, t.Name, t.Length, t.Decimals, t.Signed));
        }

        return column;
    }
}
