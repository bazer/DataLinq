using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using DataLinq.Metadata;
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
    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static void ParseInterfaces(DatabaseDefinition database)
    {
        ParseInterfacesCore(database);
    }

    internal static void ParseInterfacesCore(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels)
        {
            var model = tableModel.Model;

            if (model.ModelInstanceInterface == null)
            {
                var interfaceName = $"I{model.CsType.Name}";
                model.SetModelInstanceInterfaceCore(new CsTypeDeclaration(interfaceName, model.CsType.Namespace, ModelCsType.Interface));
            }
        }
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static Option<TableDefinition, IDLOptionFailure> ParseTable(ModelDefinition model)
    {
        return ParseTableCore(model);
    }

    internal static Option<TableDefinition, IDLOptionFailure> ParseTableCore(ModelDefinition model)
    {
        if (model == null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Model cannot be null");

        TableDefinition table;

        if (model.OriginalInterfaces.Any(x => ModelContractName.IsTableModelContract(x.Name)/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new TableDefinition(model.CsType.Name);
        else if (model.OriginalInterfaces.Any(x => ModelContractName.IsViewModelContract(x.Name)/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new ViewDefinition(model.CsType.Name);
        else
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Model '{model.CsType.Name}' does not inherit from 'ITableModel' or 'IViewModel'.");

        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                table.SetDbNameCore(tableAttribute.Name);

            if (attribute is ViewAttribute viewAttribute)
                table.SetDbNameCore(viewAttribute.Name);

            if (attribute is UseCacheAttribute useCache)
                table.SetUseCacheCore(useCache.UseCache);

            if (attribute is CacheLimitAttribute cacheLimit)
                table.CacheLimits.AddCore((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                table.IndexCache.AddCore((indexCache.Type, indexCache.Amount));

            if (table is ViewDefinition view && attribute is DefinitionAttribute definitionAttribute)
                view.SetDefinitionCore(definitionAttribute.Sql);
        }

        table.SetColumnsCore(model.ValueProperties.Values.Select(table.ParseColumnCore));

        return table;
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static void IndexColumns(DatabaseDefinition database)
    {
        IndexColumnsCore(database);
    }

    internal static void IndexColumnsCore(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Select(x => x.Table))
            for (var i = 0; i < table.ColumnCount; i++)
                table.GetColumn(i).SetIndexCore(i);
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static Option<bool, IDLOptionFailure> ParseIndices(DatabaseDefinition database)
    {
        return ParseIndicesCore(database);
    }

    internal static Option<bool, IDLOptionFailure> ParseIndicesCore(DatabaseDefinition database)
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
                    tableModel.Table.ColumnIndices.AddCore(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
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
                        existingIndex.AddColumnCore(column);
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
                        column.Table.ColumnIndices.AddCore(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
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
            if (!table.TryGetColumnByDbName(columnName, out var indexColumn))
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

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static void NormalizeDatabaseTypeName(DatabaseDefinition database)
    {
        NormalizeDatabaseTypeNameCore(database);
    }

    internal static void NormalizeDatabaseTypeNameCore(DatabaseDefinition database)
    {
        var tablePropertyNames = new HashSet<string>(
            database.TableModels.Select(x => x.CsPropertyName),
            StringComparer.Ordinal);
        var csTypeName = database.CsType.Name;

        while (tablePropertyNames.Contains(csTypeName))
            csTypeName = $"{csTypeName}Db";

        if (!string.Equals(database.CsType.Name, csTypeName, StringComparison.Ordinal))
            database.SetCsTypeCore(database.CsType.MutateName(csTypeName));
    }

    public static Option<bool, IDLOptionFailure> ValidateDatabaseObjectNames(DatabaseDefinition database)
    {
        if (string.IsNullOrWhiteSpace(database.Name))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Database '{database.CsType.Name}' has an empty database name.",
                database);

        if (string.IsNullOrWhiteSpace(database.DbName))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Database '{database.CsType.Name}' has an empty physical database name.",
                database);

        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;
            if (string.IsNullOrWhiteSpace(table.DbName))
                return CreateTableFailure(
                    table,
                    $"Model '{tableModel.Model.CsType.Name}' has an empty table or view name.");

            foreach (var column in table.Columns)
            {
                if (column is null)
                    continue;

                if (string.IsNullOrWhiteSpace(column.DbName))
                {
                    var owner = column.ValueProperty is null
                        ? $"Column on table '{table.DbName}'"
                        : $"Value property '{GetValuePropertyDisplayName(column.ValueProperty)}' on table '{table.DbName}'";

                    return CreateColumnPropertyFailure(
                        column,
                        $"{owner} has an empty column name.");
                }
            }
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateIdentityAttributeMetadata(DatabaseDefinition database)
    {
        var databaseAttributeFailure = ValidateDatabaseIdentityAttributes(database);
        if (databaseAttributeFailure is not null)
            return databaseAttributeFailure;

        foreach (var tableModel in database.TableModels)
        {
            var interfaceAttributeFailure = ValidateModelInterfaceAttributes(tableModel.Model);
            if (interfaceAttributeFailure is not null)
                return interfaceAttributeFailure;
        }

        return true;
    }

    private static IDLOptionFailure? ValidateDatabaseIdentityAttributes(DatabaseDefinition database)
    {
        var databaseAttributes = database.Attributes
            .OfType<DatabaseAttribute>()
            .ToArray();

        if (databaseAttributes.Length > 1)
            return CreateDatabaseAttributeFailure(
                database,
                databaseAttributes[1],
                $"Database '{database.CsType.Name}' has multiple [Database] attributes. Database identity metadata can define the database name only once.");

        if (databaseAttributes.SingleOrDefault() is not { } databaseAttribute)
            return null;

        if (string.IsNullOrWhiteSpace(databaseAttribute.Name))
            return CreateDatabaseAttributeFailure(
                database,
                databaseAttribute,
                $"Database '{database.CsType.Name}' has a [Database] attribute with an empty database name.");

        if (!string.Equals(databaseAttribute.Name, database.Name, StringComparison.Ordinal))
            return CreateDatabaseAttributeFailure(
                database,
                databaseAttribute,
                $"Database '{database.CsType.Name}' has [Database] name '{databaseAttribute.Name}', but linked database metadata resolves the name to '{database.Name}'.");

        return null;
    }

    private static IDLOptionFailure? ValidateModelInterfaceAttributes(ModelDefinition model)
    {
        var interfaceAttributes = model.Attributes
            .OfType<InterfaceAttribute>()
            .ToArray();

        if (interfaceAttributes.Length > 1)
            return CreateModelAttributeFailure(
                model,
                interfaceAttributes[1],
                $"Model '{model.CsType.Name}' has multiple [Interface] attributes. Model interface metadata can define one generated model-instance interface.");

        if (interfaceAttributes.SingleOrDefault() is not { } interfaceAttribute)
            return null;

        var scope = $"Model '{model.CsType.Name}'";
        if (!interfaceAttribute.GenerateInterface)
        {
            if (model.ModelInstanceInterface.HasValue)
                return CreateModelAttributeFailure(
                    model,
                    interfaceAttribute,
                    $"{scope} has [Interface] metadata with generation disabled, but linked model metadata resolves generated model-instance interface '{model.ModelInstanceInterface.Value.Name}'.");

            return null;
        }

        var interfaceName = interfaceAttribute.Name ?? $"I{model.CsType.Name}";
        if (string.IsNullOrWhiteSpace(interfaceName))
            return CreateModelAttributeFailure(
                model,
                interfaceAttribute,
                $"{scope} has [Interface] metadata with an empty generated interface name.");

        if (!IsValidCSharpIdentifier(interfaceName))
            return CreateModelAttributeFailure(
                model,
                interfaceAttribute,
                $"{scope} has [Interface] metadata with generated interface name '{interfaceName}', which is not a valid unescaped C# identifier.");

        if (!model.ModelInstanceInterface.HasValue)
            return CreateModelAttributeFailure(
                model,
                interfaceAttribute,
                $"{scope} has [Interface] metadata requesting generated interface '{interfaceName}', but linked model metadata has no generated model-instance interface.");

        if (!string.Equals(interfaceName, model.ModelInstanceInterface.Value.Name, StringComparison.Ordinal))
            return CreateModelAttributeFailure(
                model,
                interfaceAttribute,
                $"{scope} has [Interface] name '{interfaceName}', but linked model metadata resolves generated interface '{model.ModelInstanceInterface.Value.Name}'.");

        return null;
    }

    public static Option<bool, IDLOptionFailure> ValidateCSharpSymbolNames(DatabaseDefinition database)
    {
        var databaseTypeFailure = ValidateCSharpTypeDeclaration(
            database.CsType,
            $"Database '{database.DbName}'",
            database);
        if (databaseTypeFailure is not null)
            return databaseTypeFailure;

        if (database.CsType.ModelCsType != ModelCsType.Class)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Database '{database.DbName}' uses C# type kind '{database.CsType.ModelCsType}', but database types must be classes.",
                database);

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

            if (!IsValidModelDeclarationKind(model.CsType.ModelCsType))
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Model '{model.CsType.Name}' uses C# type kind '{model.CsType.ModelCsType}', but model types must be classes, records, or interfaces.",
                    model);

            foreach (var originalInterface in model.OriginalInterfaces)
            {
                var originalInterfaceFailure = ValidateCSharpTypeUsageWithNamespace(
                    originalInterface,
                    $"Model '{model.CsType.Name}' declared interface",
                    model);
                if (originalInterfaceFailure is not null)
                    return originalInterfaceFailure;

                var originalInterfaceKindFailure = ValidateCSharpTypeRole(
                    originalInterface,
                    $"Model '{model.CsType.Name}' declared interface",
                    model,
                    kind => kind == ModelCsType.Interface,
                    "an interface");
                if (originalInterfaceKindFailure is not null)
                    return originalInterfaceKindFailure;

                if (ModelContractName.TryGetInvalidModelInterfaceContractArity(
                    originalInterface.Name,
                    out var contractName,
                    out var typeArgumentCount,
                    out var expectedDescription))
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' declared DataLinq model contract '{originalInterface.Name}' with {typeArgumentCount} type arguments. '{contractName}' {expectedDescription}.",
                        model);
            }

            var modelInstanceInterfaceFailure = ValidateOptionalCSharpTypeReference(
                model.ModelInstanceInterface,
                $"Model '{model.CsType.Name}' model-instance interface",
                model);
            if (modelInstanceInterfaceFailure is not null)
                return modelInstanceInterfaceFailure;

            var modelInstanceInterfaceKindFailure = ValidateOptionalCSharpTypeRole(
                model.ModelInstanceInterface,
                $"Model '{model.CsType.Name}' model-instance interface",
                model,
                kind => kind == ModelCsType.Interface,
                "an interface");
            if (modelInstanceInterfaceKindFailure is not null)
                return modelInstanceInterfaceKindFailure;

            var immutableTypeFailure = ValidateOptionalCSharpTypeReference(
                model.ImmutableType,
                $"Model '{model.CsType.Name}' immutable type",
                model);
            if (immutableTypeFailure is not null)
                return immutableTypeFailure;

            var immutableTypeKindFailure = ValidateOptionalCSharpTypeRole(
                model.ImmutableType,
                $"Model '{model.CsType.Name}' immutable type",
                model,
                IsConcreteGeneratedModelKind,
                "a class or record");
            if (immutableTypeKindFailure is not null)
                return immutableTypeKindFailure;

            var mutableTypeFailure = ValidateOptionalCSharpTypeReference(
                model.MutableType,
                $"Model '{model.CsType.Name}' mutable type",
                model);
            if (mutableTypeFailure is not null)
                return mutableTypeFailure;

            var mutableTypeKindFailure = ValidateOptionalCSharpTypeRole(
                model.MutableType,
                $"Model '{model.CsType.Name}' mutable type",
                model,
                IsConcreteGeneratedModelKind,
                "a class or record");
            if (mutableTypeKindFailure is not null)
                return mutableTypeKindFailure;

            foreach (var modelUsing in model.Usings)
            {
                if (modelUsing is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' contains a null using namespace.",
                        model);

                if (string.IsNullOrWhiteSpace(modelUsing.FullNamespaceName))
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' contains an empty using namespace.",
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
        var typeKindFailure = ValidateCSharpTypeKind(type, scope, context);
        if (typeKindFailure is not null)
            return typeKindFailure;

        if (!IsValidCSharpIdentifier(type.Name))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# type name '{type.Name}', which is not a valid unescaped C# identifier.",
                context);

        if (string.IsNullOrWhiteSpace(type.Namespace))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} is missing a C# namespace. DataLinq generated metadata must be declared inside a namespace.",
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
        var typeKindFailure = ValidateCSharpTypeKind(type, scope, context);
        if (typeKindFailure is not null)
            return typeKindFailure;

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
        var typeKindFailure = ValidateCSharpTypeKind(type, scope, context);
        if (typeKindFailure is not null)
            return typeKindFailure;

        if (!IsValidCSharpTypeReferenceName(type.Name))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# type name '{type.Name}', which is not valid C# type syntax.",
                context);

        return null;
    }

    private static IDLOptionFailure? ValidateCSharpTypeUsageWithNamespace(
        CsTypeDeclaration type,
        string scope,
        IDefinition context)
    {
        var nameFailure = ValidateCSharpTypeUsage(type, scope, context);
        if (nameFailure is not null)
            return nameFailure;

        if (!IsValidCSharpNamespace(type.Namespace))
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} uses C# namespace '{type.Namespace}', which is not a valid unescaped C# namespace.",
                context);

        return null;
    }

    private static IDLOptionFailure? ValidateCSharpTypeKind(
        CsTypeDeclaration type,
        string scope,
        IDefinition context)
    {
        if (Enum.IsDefined(typeof(ModelCsType), type.ModelCsType))
            return null;

        return DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"{scope} uses unsupported C# type kind '{type.ModelCsType}'.",
            context);
    }

    private static IDLOptionFailure? ValidateOptionalCSharpTypeRole(
        CsTypeDeclaration? type,
        string scope,
        IDefinition context,
        Func<ModelCsType, bool> isValid,
        string expectedRole)
    {
        if (!type.HasValue || isValid(type.Value.ModelCsType))
            return null;

        return DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"{scope} uses C# type kind '{type.Value.ModelCsType}', but it must be {expectedRole}.",
            context);
    }

    private static IDLOptionFailure? ValidateCSharpTypeRole(
        CsTypeDeclaration type,
        string scope,
        IDefinition context,
        Func<ModelCsType, bool> isValid,
        string expectedRole)
    {
        if (isValid(type.ModelCsType))
            return null;

        return DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"{scope} uses C# type kind '{type.ModelCsType}', but it must be {expectedRole}.",
            context);
    }

    private static bool IsValidModelDeclarationKind(ModelCsType kind) =>
        kind is ModelCsType.Class or ModelCsType.Record or ModelCsType.Interface;

    private static bool IsConcreteGeneratedModelKind(ModelCsType kind) =>
        kind is ModelCsType.Class or ModelCsType.Record;

    private static bool IsValidCSharpNamespace(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        var namespaceName = value.AsSpan();
        var segmentStart = 0;
        for (var i = 0; i <= namespaceName.Length; i++)
        {
            if (i < namespaceName.Length && namespaceName[i] != '.')
                continue;

            if (!IsValidCSharpIdentifier(namespaceName.Slice(segmentStart, i - segmentStart)))
                return false;

            segmentStart = i + 1;
        }

        return true;
    }

    public static Option<bool, IDLOptionFailure> ValidateCacheMetadata(DatabaseDefinition database)
    {
        var databaseAttributeFailure = ValidateDatabaseCacheAttributes(database);
        if (databaseAttributeFailure is not null)
            return databaseAttributeFailure;

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

        var duplicateDatabaseLimitFailure = ValidateDuplicateCacheLimitTypes(
            database.CacheLimits,
            $"Database '{database.DbName}'",
            database);
        if (duplicateDatabaseLimitFailure is not null)
            return duplicateDatabaseLimitFailure;

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

        var duplicateDatabaseCleanupFailure = ValidateDuplicateCacheCleanupTypes(
            database.CacheCleanup,
            $"Database '{database.DbName}'",
            database);
        if (duplicateDatabaseCleanupFailure is not null)
            return duplicateDatabaseCleanupFailure;

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

        var databaseIndexCacheFailure = ValidateIndexCachePolicySet(
            database.IndexCache,
            $"Database '{database.DbName}'",
            database);
        if (databaseIndexCacheFailure is not null)
            return databaseIndexCacheFailure;

        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var table = tableModel.Table;
            var scope = $"Table '{table.DbName}'";

            var tableAttributeFailure = ValidateModelCacheAttributes(tableModel);
            if (tableAttributeFailure is not null)
                return tableAttributeFailure;

            foreach (var (limitType, amount) in table.CacheLimits)
            {
                var failure = ValidateCacheLimit(limitType, amount, scope, table);
                if (failure is not null)
                    return failure;
            }

            var duplicateTableLimitFailure = ValidateDuplicateCacheLimitTypes(
                table.CacheLimits,
                scope,
                table);
            if (duplicateTableLimitFailure is not null)
                return duplicateTableLimitFailure;

            foreach (var (indexCacheType, amount) in table.IndexCache)
            {
                var failure = ValidateIndexCachePolicy(indexCacheType, amount, scope, table);
                if (failure is not null)
                    return failure;
            }

            var tableIndexCacheFailure = ValidateIndexCachePolicySet(table.IndexCache, scope, table);
            if (tableIndexCacheFailure is not null)
                return tableIndexCacheFailure;
        }

        return true;
    }

    private static IDLOptionFailure? ValidateDatabaseCacheAttributes(DatabaseDefinition database)
    {
        if (!TryGetSingleAttribute<UseCacheAttribute>(
                database.Attributes,
                out var useCacheAttribute,
                out var duplicateUseCacheAttribute))
        {
            return CreateDatabaseAttributeFailure(
                database,
                duplicateUseCacheAttribute!,
                $"Database '{database.DbName}' has multiple [UseCache] attributes. Database cache metadata can define the cache flag only once.");
        }

        if (useCacheAttribute is not null &&
            useCacheAttribute.UseCache != database.UseCache)
        {
            return CreateDatabaseAttributeFailure(
                database,
                useCacheAttribute,
                $"Database '{database.DbName}' has [UseCache] value '{useCacheAttribute.UseCache}', but linked cache metadata resolves to '{database.UseCache}'.");
        }

        var cacheLimitFailure = ValidateCacheLimitAttributes(
            database.Attributes,
            database.CacheLimits,
            $"Database '{database.DbName}'",
            (attribute, message) => CreateDatabaseAttributeFailure(database, attribute, message));
        if (cacheLimitFailure is not null)
            return cacheLimitFailure;

        var cacheCleanupFailure = ValidateCacheCleanupAttributes(
            database.Attributes,
            database.CacheCleanup,
            $"Database '{database.DbName}'",
            (attribute, message) => CreateDatabaseAttributeFailure(database, attribute, message));
        if (cacheCleanupFailure is not null)
            return cacheCleanupFailure;

        return ValidateIndexCacheAttributes(
            database.Attributes,
            database.IndexCache,
            $"Database '{database.DbName}'",
            (attribute, message) => CreateDatabaseAttributeFailure(database, attribute, message));
    }

    private static IDLOptionFailure? ValidateModelCacheAttributes(TableModel tableModel)
    {
        var model = tableModel.Model;
        var table = tableModel.Table;
        var scope = $"Model '{model.CsType.Name}'";
        if (!TryGetSingleAttribute<UseCacheAttribute>(
                model.Attributes,
                out var useCacheAttribute,
                out var duplicateUseCacheAttribute))
        {
            return CreateModelAttributeFailure(
                model,
                duplicateUseCacheAttribute!,
                $"{scope} has multiple [UseCache] attributes. Table cache metadata can define an explicit cache override only once.");
        }

        if (useCacheAttribute is not null)
        {
            if (!table.explicitUseCache.HasValue)
                return CreateModelAttributeFailure(
                    model,
                    useCacheAttribute,
                    $"{scope} has [UseCache] metadata, but linked table '{table.DbName}' has no explicit cache override.");

            if (useCacheAttribute.UseCache != table.explicitUseCache.Value)
                return CreateModelAttributeFailure(
                    model,
                    useCacheAttribute,
                    $"{scope} has [UseCache] value '{useCacheAttribute.UseCache}', but linked table '{table.DbName}' resolves its explicit cache override to '{table.explicitUseCache.Value}'.");
        }

        if (FirstAttributeOrDefault<CacheCleanupAttribute>(model.Attributes) is { } cacheCleanupAttribute)
            return CreateModelAttributeFailure(
                model,
                cacheCleanupAttribute,
                $"{scope} has [CacheCleanup] metadata, but cache cleanup is database-scoped and is not supported on table models.");

        var cacheLimitFailure = ValidateCacheLimitAttributes(
            model.Attributes,
            table.CacheLimits,
            scope,
            (attribute, message) => CreateModelAttributeFailure(model, attribute, message));
        if (cacheLimitFailure is not null)
            return cacheLimitFailure;

        return ValidateIndexCacheAttributes(
            model.Attributes,
            table.IndexCache,
            scope,
            (attribute, message) => CreateModelAttributeFailure(model, attribute, message));
    }

    private static IDLOptionFailure? ValidateCacheLimitAttributes(
        IReadOnlyList<Attribute> attributes,
        IReadOnlyList<(CacheLimitType limitType, long amount)> metadata,
        string scope,
        Func<Attribute, string, IDLOptionFailure> createFailure)
    {
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not CacheLimitAttribute attribute)
                continue;

            if (!Enum.IsDefined(typeof(CacheLimitType), attribute.LimitType))
                return createFailure(
                    attribute,
                    $"{scope} has [CacheLimit] metadata with unsupported cache limit type '{attribute.LimitType}'.");

            if (attribute.Amount <= 0)
                return createFailure(
                    attribute,
                    $"{scope} has [CacheLimit] metadata for '{attribute.LimitType}' with amount '{attribute.Amount}'. Cache limit amounts must be greater than zero.");
        }

        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not CacheLimitAttribute attribute)
                continue;

            for (var previousIndex = 0; previousIndex < i; previousIndex++)
            {
                if (attributes[previousIndex] is CacheLimitAttribute previous &&
                    previous.LimitType == attribute.LimitType)
                {
                    return createFailure(
                        attribute,
                        $"{scope} has multiple [CacheLimit] attributes for '{attribute.LimitType}'. Cache limits can include multiple limit types, but each type can be configured only once.");
                }
            }
        }

        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not CacheLimitAttribute attribute)
                continue;

            if (metadata.Contains((attribute.LimitType, attribute.Amount)))
                continue;

            var matchingLimit = metadata.FirstOrDefault(limit => limit.limitType == attribute.LimitType);
            var message = matchingLimit != default
                ? $"{scope} has [CacheLimit] attribute for '{attribute.LimitType}' amount '{attribute.Amount}', but linked cache metadata resolves that limit to '{matchingLimit.amount}'."
                : $"{scope} has [CacheLimit] attribute for '{attribute.LimitType}' amount '{attribute.Amount}', but linked cache metadata does not contain that policy.";

            return createFailure(attribute, message);
        }

        return null;
    }

    private static IDLOptionFailure? ValidateCacheCleanupAttributes(
        IReadOnlyList<Attribute> attributes,
        IReadOnlyList<(CacheCleanupType cleanupType, long amount)> metadata,
        string scope,
        Func<Attribute, string, IDLOptionFailure> createFailure)
    {
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not CacheCleanupAttribute attribute)
                continue;

            if (!Enum.IsDefined(typeof(CacheCleanupType), attribute.LimitType))
                return createFailure(
                    attribute,
                    $"{scope} has [CacheCleanup] metadata with unsupported cache cleanup type '{attribute.LimitType}'.");

            if (attribute.Amount <= 0)
                return createFailure(
                    attribute,
                    $"{scope} has [CacheCleanup] metadata for '{attribute.LimitType}' with amount '{attribute.Amount}'. Cache cleanup amounts must be greater than zero.");
        }

        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not CacheCleanupAttribute attribute)
                continue;

            for (var previousIndex = 0; previousIndex < i; previousIndex++)
            {
                if (attributes[previousIndex] is CacheCleanupAttribute previous &&
                    previous.LimitType == attribute.LimitType)
                {
                    return createFailure(
                        attribute,
                        $"{scope} has multiple [CacheCleanup] attributes for '{attribute.LimitType}'. Cache cleanup can configure each cleanup type only once.");
                }
            }
        }

        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not CacheCleanupAttribute attribute)
                continue;

            if (metadata.Contains((attribute.LimitType, attribute.Amount)))
                continue;

            var matchingCleanup = metadata.FirstOrDefault(cleanup => cleanup.cleanupType == attribute.LimitType);
            var message = matchingCleanup != default
                ? $"{scope} has [CacheCleanup] attribute for '{attribute.LimitType}' amount '{attribute.Amount}', but linked cache metadata resolves that cleanup to '{matchingCleanup.amount}'."
                : $"{scope} has [CacheCleanup] attribute for '{attribute.LimitType}' amount '{attribute.Amount}', but linked cache metadata does not contain that policy.";

            return createFailure(attribute, message);
        }

        return null;
    }

    private static IDLOptionFailure? ValidateIndexCacheAttributes(
        IReadOnlyList<Attribute> attributes,
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> metadata,
        string scope,
        Func<Attribute, string, IDLOptionFailure> createFailure)
    {
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not IndexCacheAttribute attribute)
                continue;

            if (!Enum.IsDefined(typeof(IndexCacheType), attribute.Type))
                return createFailure(
                    attribute,
                    $"{scope} has [IndexCache] metadata with unsupported index-cache type '{attribute.Type}'.");

            if (attribute.Type == IndexCacheType.MaxAmountRows && !attribute.Amount.HasValue)
                return createFailure(
                    attribute,
                    $"{scope} has MaxAmountRows [IndexCache] metadata without a row amount.");

            if (attribute.Amount is <= 0)
                return createFailure(
                    attribute,
                    $"{scope} has [IndexCache] metadata for '{attribute.Type}' with amount '{attribute.Amount}'. Index-cache amounts must be greater than zero.");

            if (attribute.Type != IndexCacheType.MaxAmountRows && attribute.Amount.HasValue)
                return createFailure(
                    attribute,
                    $"{scope} has [IndexCache] metadata for '{attribute.Type}' with amount '{attribute.Amount}', but only MaxAmountRows can specify an amount.");
        }

        IndexCacheAttribute? firstPolicy = null;
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not IndexCacheAttribute attribute)
                continue;

            if (firstPolicy is null)
            {
                firstPolicy = attribute;
            }
            else
            {
                if (firstPolicy.Type == attribute.Type)
                {
                    return createFailure(
                        attribute,
                        $"{scope} has multiple [IndexCache] attributes for '{attribute.Type}'. A cache scope can configure each index-cache policy type only once.");
                }

                return createFailure(
                    attribute,
                    $"{scope} has conflicting [IndexCache] policies '{firstPolicy.Type}, {attribute.Type}'. A cache scope can use only one index-cache policy.");
            }
        }

        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not IndexCacheAttribute attribute)
                continue;

            if (metadata.Contains((attribute.Type, attribute.Amount)))
                continue;

            var matchingPolicy = metadata.FirstOrDefault(policy => policy.indexCacheType == attribute.Type);
            var message = matchingPolicy != default
                ? $"{scope} has [IndexCache] attribute for '{attribute.Type}' amount '{FormatNullableValue(attribute.Amount)}', but linked cache metadata resolves that policy to '{FormatNullableValue(matchingPolicy.amount)}'."
                : $"{scope} has [IndexCache] attribute for '{attribute.Type}' amount '{FormatNullableValue(attribute.Amount)}', but linked cache metadata does not contain that policy.";

            return createFailure(attribute, message);
        }

        return null;
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

        if (indexCacheType != IndexCacheType.MaxAmountRows && amount.HasValue)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} has index-cache metadata for '{indexCacheType}' with amount '{amount}', but only MaxAmountRows can specify an amount.",
                context);

        return null;
    }

    private static IDLOptionFailure? ValidateDuplicateCacheLimitTypes(
        IReadOnlyList<(CacheLimitType limitType, long amount)> limits,
        string scope,
        IDefinition context)
    {
        var duplicateGroup = limits
            .GroupBy(x => x.limitType)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateGroup is null)
            return null;

        return DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"{scope} defines multiple cache limit entries for '{duplicateGroup.Key}'. Cache limits can include multiple limit types, but each type can be configured only once.",
            context);
    }

    private static IDLOptionFailure? ValidateDuplicateCacheCleanupTypes(
        IReadOnlyList<(CacheCleanupType cleanupType, long amount)> cleanup,
        string scope,
        IDefinition context)
    {
        var duplicateGroup = cleanup
            .GroupBy(x => x.cleanupType)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateGroup is null)
            return null;

        return DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"{scope} defines multiple cache cleanup entries for '{duplicateGroup.Key}'. Cache cleanup can configure each cleanup type only once.",
            context);
    }

    private static IDLOptionFailure? ValidateIndexCachePolicySet(
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> policies,
        string scope,
        IDefinition context)
    {
        var duplicateGroup = policies
            .GroupBy(x => x.indexCacheType)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateGroup is not null)
        {
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"{scope} defines multiple index-cache entries for '{duplicateGroup.Key}'. A cache scope can configure each index-cache policy type only once.",
                context);
        }

        var policyTypes = policies
            .Select(x => x.indexCacheType)
            .Distinct()
            .ToArray();

        if (policyTypes.Length <= 1)
            return null;

        return DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"{scope} defines conflicting index-cache policies '{policyTypes.ToJoinedString(", ")}'. A cache scope can use only one index-cache policy.",
            context);
    }

    public static Option<bool, IDLOptionFailure> ValidateProviderScopedAttributeDatabaseTypes(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var model = tableModel.Model;

            foreach (var comment in model.Attributes.OfType<CommentAttribute>())
            {
                if (!IsValidProviderScopedDatabaseType(comment.DatabaseType))
                    return CreateModelAttributeFailure(
                        model,
                        comment,
                        $"Comment attribute on model '{model.CsType.Name}' uses unsupported database type '{comment.DatabaseType}'.");
            }

            foreach (var check in model.Attributes.OfType<CheckAttribute>())
            {
                if (!IsValidProviderScopedDatabaseType(check.DatabaseType))
                    return CreateModelAttributeFailure(
                        model,
                        check,
                        $"Check attribute on model '{model.CsType.Name}' uses unsupported database type '{check.DatabaseType}'.");
            }

            foreach (var property in model.ValueProperties.Values)
            {
                foreach (var comment in property.Attributes.OfType<CommentAttribute>())
                {
                    if (!IsValidProviderScopedDatabaseType(comment.DatabaseType))
                        return CreateValuePropertyAttributeFailure(
                            property,
                            comment,
                            $"Comment attribute on value property '{GetValuePropertyDisplayName(property)}' uses unsupported database type '{comment.DatabaseType}'.");
                }

                foreach (var defaultSql in property.Attributes.OfType<DefaultSqlAttribute>())
                {
                    if (!IsValidProviderScopedDatabaseType(defaultSql.DatabaseType))
                        return CreateValuePropertyAttributeFailure(
                            property,
                            defaultSql,
                            $"Default SQL attribute on value property '{GetValuePropertyDisplayName(property)}' uses unsupported database type '{defaultSql.DatabaseType}'.");
                }
            }
        }

        return true;
    }

    private static bool IsValidProviderScopedDatabaseType(DatabaseType databaseType) =>
        databaseType != DatabaseType.Unknown &&
        Enum.IsDefined(typeof(DatabaseType), databaseType);

    public static Option<bool, IDLOptionFailure> ValidateSchemaAnnotationMetadata(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var model = tableModel.Model;

            foreach (var comment in model.Attributes.OfType<CommentAttribute>())
            {
                if (comment.Text is null)
                    return CreateModelAttributeFailure(
                        model,
                        comment,
                        $"Comment attribute on model '{model.CsType.Name}' has a null comment text.");
            }

            foreach (var check in model.Attributes.OfType<CheckAttribute>())
            {
                var failure = ValidateCheckAttribute(model, check);
                if (failure is not null)
                    return failure;
            }

            var duplicateCheckGroup = model.Attributes
                .OfType<CheckAttribute>()
                .Where(x => Enum.IsDefined(typeof(DatabaseType), x.DatabaseType) && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => new { x.DatabaseType, x.Name })
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicateCheckGroup is not null)
            {
                var duplicate = duplicateCheckGroup.Skip(1).First();
                return CreateModelAttributeFailure(
                    model,
                    duplicate,
                    $"Model '{model.CsType.Name}' defines duplicate check constraint '{duplicateCheckGroup.Key.Name}' for database type '{duplicateCheckGroup.Key.DatabaseType}'.");
            }

            foreach (var property in model.ValueProperties.Values)
            {
                foreach (var comment in property.Attributes.OfType<CommentAttribute>())
                {
                    if (comment.Text is null)
                        return CreateValuePropertyAttributeFailure(
                            property,
                            comment,
                            $"Comment attribute on value property '{GetValuePropertyDisplayName(property)}' has a null comment text.");
                }
            }
        }

        return true;
    }

    private static IDLOptionFailure? ValidateCheckAttribute(ModelDefinition model, CheckAttribute attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute.Name))
            return CreateModelAttributeFailure(
                model,
                attribute,
                $"Check attribute on model '{model.CsType.Name}' has an empty check constraint name.");

        if (string.IsNullOrWhiteSpace(attribute.Expression))
            return CreateModelAttributeFailure(
                model,
                attribute,
                $"Check attribute on model '{model.CsType.Name}' has an empty check expression.");

        return null;
    }

    public static Option<bool, IDLOptionFailure> ValidateRelationalAttributeMetadata(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels)
        {
            if (tableModel.IsStub)
                continue;

            var model = tableModel.Model;

            foreach (var attribute in model.Attributes)
            {
                if (attribute is not IndexAttribute index)
                    continue;

                var failure = ValidateIndexAttribute(
                    index,
                    $"Class-level index attribute on model '{model.CsType.Name}'",
                    requiresColumns: true,
                    message => CreateModelAttributeFailure(model, index, message));
                if (failure is not null)
                    return failure;
            }

            foreach (var property in model.ValueProperties.Values)
            {
                foreach (var attribute in property.Attributes)
                {
                    if (attribute is IndexAttribute index)
                    {
                        var failure = ValidateIndexAttribute(
                            index,
                            $"Index attribute on value property '{GetValuePropertyDisplayName(property)}'",
                            requiresColumns: false,
                            message => CreateValuePropertyAttributeFailure(property, index, message));
                        if (failure is not null)
                            return failure;
                    }

                    if (attribute is ForeignKeyAttribute foreignKey)
                    {
                        var failure = ValidateForeignKeyAttribute(property, foreignKey);
                        if (failure is not null)
                            return failure;
                    }
                }
            }

            var foreignKeyConstraintFailure = ValidateForeignKeyConstraintAttributeMetadata(tableModel);
            if (foreignKeyConstraintFailure is not null)
                return foreignKeyConstraintFailure;

            foreach (var property in model.RelationProperties.Values)
            {
                if (!TryGetSingleAttribute<RelationAttribute>(
                        property.Attributes,
                        out var relationAttribute,
                        out var duplicateRelationAttribute))
                {
                    return CreateRelationPropertyFailure(
                        property,
                        duplicateRelationAttribute,
                        $"Relation property '{GetRelationPropertyDisplayName(property)}' has multiple [Relation] attributes. A relation property can identify only one database relation.");
                }

                if (relationAttribute is not null)
                {
                    var failure = ValidateRelationAttribute(property, relationAttribute);
                    if (failure is not null)
                        return failure;
                }
            }

            var repeatedIndexFailure = ValidateRepeatedIndexAttributeMetadata(tableModel);
            if (repeatedIndexFailure is not null)
                return repeatedIndexFailure;
        }

        return true;
    }

    private static IDLOptionFailure? ValidateIndexAttribute(
        IndexAttribute attribute,
        string scope,
        bool requiresColumns,
        Func<string, IDLOptionFailure> createFailure)
    {
        if (string.IsNullOrWhiteSpace(attribute.Name))
            return createFailure($"{scope} has an empty index name.");

        if (!Enum.IsDefined(typeof(IndexCharacteristic), attribute.Characteristic))
            return createFailure($"{scope} uses unsupported index characteristic '{attribute.Characteristic}'.");

        if (!Enum.IsDefined(typeof(IndexType), attribute.Type))
            return createFailure($"{scope} uses unsupported index type '{attribute.Type}'.");

        var columns = attribute.Columns;
        if (columns is null)
            return createFailure($"{scope} has a null index column collection.");

        if (requiresColumns && columns.Length == 0)
            return createFailure($"{scope} must specify its columns. IndexAttribute.Columns expects database column names.");

        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column))
                return createFailure($"{scope} contains an empty index column name. IndexAttribute.Columns expects database column names.");
        }

        return null;
    }

    private static IDLOptionFailure? ValidateRepeatedIndexAttributeMetadata(TableModel tableModel)
    {
        List<(IndexAttribute Attribute, ValueProperty? Property)>? definitions = null;
        var model = tableModel.Model;

        foreach (var attribute in model.Attributes)
        {
            if (attribute is IndexAttribute indexAttribute)
            {
                definitions ??= [];
                definitions.Add((indexAttribute, null));
            }
        }

        foreach (var property in model.ValueProperties.Values)
        {
            foreach (var attribute in property.Attributes)
            {
                if (attribute is IndexAttribute indexAttribute)
                {
                    definitions ??= [];
                    definitions.Add((indexAttribute, property));
                }
            }
        }

        if (definitions is null || definitions.Count <= 1)
            return null;

        var validatedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < definitions.Count; i++)
        {
            var first = definitions[i];
            if (!validatedNames.Add(first.Attribute.Name))
                continue;

            List<(IndexAttribute Attribute, ValueProperty? Property)>? group = null;
            for (var candidateIndex = i + 1; candidateIndex < definitions.Count; candidateIndex++)
            {
                var candidate = definitions[candidateIndex];
                if (!string.Equals(candidate.Attribute.Name, first.Attribute.Name, StringComparison.Ordinal))
                    continue;

                if (candidate.Attribute.Characteristic != first.Attribute.Characteristic ||
                    candidate.Attribute.Type != first.Attribute.Type)
                {
                    var message = $"Index attribute '{candidate.Attribute.Name}' on table '{tableModel.Table.DbName}' uses characteristic '{candidate.Attribute.Characteristic}' and type '{candidate.Attribute.Type}', but another attribute with the same name uses characteristic '{first.Attribute.Characteristic}' and type '{first.Attribute.Type}'. Repeated index attributes with the same name on a table must agree on characteristic and type.";
                    return CreateIndexAttributeFailure(
                        model,
                        candidate.Property,
                        candidate.Attribute,
                        message);
                }

                group ??= [first];
                group.Add(candidate);
            }

            if (group is null)
                continue;

            var columnConflict = ValidateRepeatedIndexAttributeColumns(
                tableModel,
                model,
                group);
            if (columnConflict is not null)
                return columnConflict;
        }

        return null;
    }

    private static IDLOptionFailure? ValidateRepeatedIndexAttributeColumns(
        TableModel tableModel,
        ModelDefinition model,
        IEnumerable<(IndexAttribute Attribute, ValueProperty? Property)> group)
    {
        var definitions = group.ToList();
        var explicitDefinitions = definitions
            .Where(definition => definition.Attribute.Columns.Length > 0)
            .ToList();

        if (explicitDefinitions.Count == 0)
            return null;

        var first = explicitDefinitions[0];
        var conflict = explicitDefinitions
            .Skip(1)
            .FirstOrDefault(definition => !IndexAttributeColumnsMatch(definition.Attribute.Columns, first.Attribute.Columns));

        if (conflict.Attribute is not null)
        {
            var message = $"Index attribute '{conflict.Attribute.Name}' on table '{tableModel.Table.DbName}' targets columns '{conflict.Attribute.Columns.ToJoinedString(", ")}', but another attribute with the same name targets columns '{first.Attribute.Columns.ToJoinedString(", ")}'. Repeated index attributes with explicit columns must agree on column order.";
            return CreateIndexAttributeFailure(
                model,
                conflict.Property,
                conflict.Attribute,
                message);
        }

        var implicitConflict = definitions
            .Where(definition => definition.Property is not null && definition.Attribute.Columns.Length == 0)
            .FirstOrDefault(definition =>
                definition.Property?.Column?.DbName is not { } columnName ||
                !first.Attribute.Columns.Contains(columnName, StringComparer.Ordinal));

        if (implicitConflict.Attribute is null)
            return null;

        var propertyName = implicitConflict.Property is null
            ? "<unknown>"
            : GetValuePropertyDisplayName(implicitConflict.Property);
        var implicitMessage = $"Index attribute '{implicitConflict.Attribute.Name}' on value property '{propertyName}' does not specify explicit columns, but another attribute with the same name targets columns '{first.Attribute.Columns.ToJoinedString(", ")}'. Mixed implicit and explicit repeated index attributes must only appear on columns included in the explicit index target.";
        return CreateIndexAttributeFailure(
            model,
            implicitConflict.Property,
            implicitConflict.Attribute,
            implicitMessage);
    }

    private static bool IndexAttributeColumnsMatch(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static IDLOptionFailure CreateIndexAttributeFailure(
        ModelDefinition model,
        ValueProperty? property,
        IndexAttribute attribute,
        string message)
    {
        if (property?.Column is { } column)
            return CreateIndexFailure(column, attribute, message);

        if (property is not null)
            return CreateValuePropertyAttributeFailure(property, attribute, message);

        return CreateIndexFailure(model, attribute, message);
    }

    private static IDLOptionFailure? ValidateForeignKeyAttribute(
        ValueProperty property,
        ForeignKeyAttribute attribute)
    {
        var scope = $"Foreign key attribute on value property '{GetValuePropertyDisplayName(property)}'";

        if (string.IsNullOrWhiteSpace(attribute.Table))
            return CreateValuePropertyAttributeFailure(property, attribute, $"{scope} has an empty referenced table name.");

        if (string.IsNullOrWhiteSpace(attribute.Column))
            return CreateValuePropertyAttributeFailure(property, attribute, $"{scope} has an empty referenced column name.");

        if (string.IsNullOrWhiteSpace(attribute.Name))
            return CreateValuePropertyAttributeFailure(property, attribute, $"{scope} has an empty constraint name.");

        if (attribute.Ordinal < 0)
            return CreateValuePropertyAttributeFailure(property, attribute, $"{scope} uses negative ordinal '{attribute.Ordinal}'. Foreign-key ordinals must be nonnegative.");

        if (!Enum.IsDefined(typeof(ReferentialAction), attribute.OnUpdate))
            return CreateValuePropertyAttributeFailure(property, attribute, $"{scope} uses unsupported on-update action '{attribute.OnUpdate}'.");

        if (!Enum.IsDefined(typeof(ReferentialAction), attribute.OnDelete))
            return CreateValuePropertyAttributeFailure(property, attribute, $"{scope} uses unsupported on-delete action '{attribute.OnDelete}'.");

        return null;
    }

    private static IDLOptionFailure? ValidateForeignKeyConstraintAttributeMetadata(TableModel tableModel)
    {
        List<(ValueProperty Property, ForeignKeyAttribute Attribute)>? definitions = null;
        foreach (var property in tableModel.Model.ValueProperties.Values)
        {
            foreach (var attribute in property.Attributes)
            {
                if (attribute is not ForeignKeyAttribute foreignKey)
                    continue;

                definitions ??= [];
                definitions.Add((property, foreignKey));
            }
        }

        if (definitions is null)
            return null;

        for (var i = 0; i < definitions.Count; i++)
        {
            var current = definitions[i];
            for (var previousIndex = 0; previousIndex < i; previousIndex++)
            {
                var previous = definitions[previousIndex];
                if (!string.Equals(current.Attribute.Name, previous.Attribute.Name, StringComparison.Ordinal))
                    continue;

                if (ReferenceEquals(current.Property, previous.Property))
                {
                    return CreateValuePropertyAttributeFailure(
                        current.Property,
                        current.Attribute,
                        $"Value property '{GetValuePropertyDisplayName(current.Property)}' has multiple [ForeignKey] attributes with constraint name '{current.Attribute.Name}'. A single value property can contribute only one column to a foreign-key constraint.");
                }

                if (!string.Equals(current.Attribute.Table, previous.Attribute.Table, StringComparison.Ordinal))
                {
                    return CreateValuePropertyAttributeFailure(
                        current.Property,
                        current.Attribute,
                        $"Foreign key attribute '{current.Attribute.Name}' on table '{tableModel.Table.DbName}' references table '{current.Attribute.Table}', but another attribute with the same constraint name references table '{previous.Attribute.Table}'. Foreign-key attributes with the same constraint name on a table must target one referenced table.");
                }

                if (current.Attribute.OnUpdate != previous.Attribute.OnUpdate ||
                    current.Attribute.OnDelete != previous.Attribute.OnDelete)
                {
                    return CreateValuePropertyAttributeFailure(
                        current.Property,
                        current.Attribute,
                        $"Foreign key attribute '{current.Attribute.Name}' on table '{tableModel.Table.DbName}' uses on-update action '{current.Attribute.OnUpdate}' and on-delete action '{current.Attribute.OnDelete}', but another attribute with the same constraint name uses on-update action '{previous.Attribute.OnUpdate}' and on-delete action '{previous.Attribute.OnDelete}'. Foreign-key attributes with the same constraint name must agree on referential actions.");
                }
            }
        }

        return null;
    }

    private static IDLOptionFailure? ValidateRelationAttribute(
        RelationProperty property,
        RelationAttribute attribute)
    {
        var scope = $"Relation attribute on relation property '{GetRelationPropertyDisplayName(property)}'";

        if (string.IsNullOrWhiteSpace(attribute.Table))
            return CreateRelationPropertyFailure(property, attribute, $"{scope} has an empty referenced table name.");

        var columns = attribute.Columns;
        if (columns is null || columns.Length == 0)
            return CreateRelationPropertyFailure(property, attribute, $"{scope} must specify at least one referenced column.");

        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column))
                return CreateRelationPropertyFailure(property, attribute, $"{scope} contains an empty referenced column name.");
        }

        if (attribute.Name != null && string.IsNullOrWhiteSpace(attribute.Name))
            return CreateRelationPropertyFailure(property, attribute, $"{scope} has an empty constraint name.");

        return null;
    }

    private static IDLOptionFailure CreateDatabaseAttributeFailure(DatabaseDefinition database, Attribute attribute, string message)
    {
        var attributeLocation = database.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var databaseLocation = database.GetSourceLocation();
        if (databaseLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, databaseLocation.Value);

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, database);
    }

    private static IDLOptionFailure CreateModelAttributeFailure(ModelDefinition model, Attribute attribute, string message)
    {
        var attributeLocation = model.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var modelLocation = model.GetSourceLocation();
        if (modelLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, modelLocation.Value);

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, model);
    }

    private static IDLOptionFailure CreateValuePropertyAttributeFailure(ValueProperty property, Attribute attribute, string message)
    {
        var attributeLocation = property.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        return CreateValuePropertyFailure(property, message);
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

            if (tableModel.Database is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Table model '{tableModel.CsPropertyName}' is registered on database '{database.DbName}', but has no owning database.",
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

            if (!Enum.IsDefined(typeof(TableType), tableModel.Table.Type))
                return CreateTableFailure(
                    tableModel.Table,
                    $"Table '{tableModel.Table.DbName}' on model '{tableModel.Model.CsType.Name}' uses unsupported table type '{tableModel.Table.Type}'.");

            if (tableModel.Table.Type == TableType.View && tableModel.Table is not ViewDefinition)
                return CreateTableFailure(
                    tableModel.Table,
                    $"Table '{tableModel.Table.DbName}' on model '{tableModel.Model.CsType.Name}' is marked as a view, but its metadata is not a view definition.");

            if (tableModel.Table.Type == TableType.Table && tableModel.Table is ViewDefinition)
                return CreateTableFailure(
                    tableModel.Table,
                    $"View '{tableModel.Table.DbName}' on model '{tableModel.Model.CsType.Name}' is marked as a table, but view metadata must use table type '{TableType.View}'.");

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

            var tableAttributeValidation = ValidateModelTableAttributes(tableModel);
            if (tableAttributeValidation is not null)
                return tableAttributeValidation;
        }

        return true;
    }

    private static IDLOptionFailure? ValidateModelTableAttributes(TableModel tableModel)
    {
        var model = tableModel.Model;
        var table = tableModel.Table;
        var identityAttributes = model.Attributes
            .Where(attribute => attribute is TableAttribute or ViewAttribute)
            .ToArray();

        if (identityAttributes.Length > 1)
            return CreateModelAttributeFailure(
                model,
                identityAttributes[1],
                $"Model '{model.CsType.Name}' has multiple table identity attributes. Use at most one [Table] or [View] attribute because the linked table metadata has a single table/view shape.");

        if (identityAttributes.SingleOrDefault() is TableAttribute tableAttribute)
        {
            if (table is ViewDefinition)
                return CreateModelAttributeFailure(
                    model,
                    tableAttribute,
                    $"Model '{model.CsType.Name}' has [Table] metadata, but linked object '{table.DbName}' is a view. Model table identity attributes must match the linked table metadata.");

            if (!string.Equals(tableAttribute.Name, table.DbName, StringComparison.Ordinal))
                return CreateModelAttributeFailure(
                    model,
                    tableAttribute,
                    $"Model '{model.CsType.Name}' has [Table] name '{tableAttribute.Name}', but linked table is '{table.DbName}'.");
        }

        if (identityAttributes.SingleOrDefault() is ViewAttribute viewAttribute)
        {
            if (table is not ViewDefinition)
                return CreateModelAttributeFailure(
                    model,
                    viewAttribute,
                    $"Model '{model.CsType.Name}' has [View] metadata, but linked object '{table.DbName}' is a table. Model table identity attributes must match the linked table metadata.");

            if (!string.Equals(viewAttribute.Name, table.DbName, StringComparison.Ordinal))
                return CreateModelAttributeFailure(
                    model,
                    viewAttribute,
                    $"Model '{model.CsType.Name}' has [View] name '{viewAttribute.Name}', but linked view is '{table.DbName}'.");
        }

        var definitionAttributes = model.Attributes
            .OfType<DefinitionAttribute>()
            .ToArray();

        if (definitionAttributes.Length > 1)
            return CreateModelAttributeFailure(
                model,
                definitionAttributes[1],
                $"Model '{model.CsType.Name}' has multiple [Definition] attributes. Use a single view definition because the linked view metadata has one SQL definition.");

        if (definitionAttributes.SingleOrDefault() is not { } definitionAttribute)
            return null;

        if (table is not ViewDefinition view)
            return CreateModelAttributeFailure(
                model,
                definitionAttribute,
                $"Model '{model.CsType.Name}' has [Definition] metadata, but linked object '{table.DbName}' is a table. Definitions are valid only for views.");

        if (!string.Equals(definitionAttribute.Sql, view.Definition, StringComparison.Ordinal))
            return CreateModelAttributeFailure(
                model,
                definitionAttribute,
                $"View model '{model.CsType.Name}' has [Definition] SQL '{definitionAttribute.Sql}', but linked view '{view.DbName}' defines '{view.Definition}'.");

        return null;
    }

    public static Option<bool, IDLOptionFailure> ValidateMetadataCollections(DatabaseDefinition database)
    {
        foreach (var attribute in database.Attributes)
        {
            if (attribute is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Database '{database.DbName}' contains a null attribute.",
                    database);
        }

        foreach (var tableModel in database.TableModels)
        {
            if (tableModel?.Model is not { } model)
                continue;

            foreach (var attribute in model.Attributes)
            {
                if (attribute is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' contains a null attribute.",
                        model);
            }

            foreach (var entry in model.ValueProperties)
            {
                var propertyName = entry.Key;
                var property = entry.Value;

                if (property is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' contains a null value property for key '{propertyName}'.",
                        model);

                var propertyMetadataFailure = ValidatePropertyTypeMetadata(
                    property,
                    PropertyType.Value,
                    $"Value property '{GetValuePropertyDisplayName(property)}'",
                    message => CreateValuePropertyFailure(property, message));
                if (propertyMetadataFailure is not null)
                    return propertyMetadataFailure;

                foreach (var attribute in property.Attributes)
                {
                    if (attribute is null)
                        return CreateValuePropertyFailure(
                            property,
                            $"Value property '{GetValuePropertyDisplayName(property)}' contains a null attribute.");
                }
            }

            foreach (var entry in model.RelationProperties)
            {
                var propertyName = entry.Key;
                var property = entry.Value;

                if (property is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Model '{model.CsType.Name}' contains a null relation property for key '{propertyName}'.",
                        model);

                var propertyMetadataFailure = ValidatePropertyTypeMetadata(
                    property,
                    PropertyType.Relation,
                    $"Relation property '{GetRelationPropertyDisplayName(property)}'",
                    message => CreateRelationPropertyFailure(property, null, message));
                if (propertyMetadataFailure is not null)
                    return propertyMetadataFailure;

                foreach (var attribute in property.Attributes)
                {
                    if (attribute is null)
                        return CreateRelationPropertyFailure(
                            property,
                            null,
                            $"Relation property '{GetRelationPropertyDisplayName(property)}' contains a null attribute.");
                }
            }
        }

        return true;
    }

    private static IDLOptionFailure? ValidatePropertyTypeMetadata(
        PropertyDefinition property,
        PropertyType expectedType,
        string scope,
        Func<string, IDLOptionFailure> createFailure)
    {
        if (!Enum.IsDefined(typeof(PropertyType), property.Type))
            return createFailure($"{scope} uses unsupported property type '{property.Type}'.");

        if (property.Type != expectedType)
            return createFailure($"{scope} is stored as a {expectedType.ToString().ToLowerInvariant()} property, but is marked as '{property.Type}'.");

        return null;
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
                        $"Table '{table.DbName}' has primary-key column '{primaryKeyColumn.Table?.DbName ?? "<unknown>"}.{primaryKeyColumn.DbName}', but primary-key columns must belong to the table.");

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

                if (!Enum.IsDefined(typeof(IndexCharacteristic), index.Characteristic))
                    return CreateColumnIndexFailure(
                        table,
                        $"Index '{index.Name}' on table '{table.DbName}' uses unsupported index characteristic '{index.Characteristic}'.");

                if (!Enum.IsDefined(typeof(IndexType), index.Type))
                    return CreateColumnIndexFailure(
                        table,
                        $"Index '{index.Name}' on table '{table.DbName}' uses unsupported index type '{index.Type}'.");

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

                if (index.RelationParts is null)
                    return CreateColumnIndexFailure(
                        table,
                        $"Index '{index.Name}' on table '{table.DbName}' has a null relation-part collection.");

                foreach (var column in index.Columns)
                {
                    if (column is null)
                        return CreateColumnIndexFailure(
                            table,
                            $"Index '{index.Name}' on table '{table.DbName}' contains a null column reference.");

                    if (!ReferenceEquals(column.Table, table))
                        return CreateColumnIndexFailure(
                            table,
                            $"Index '{index.Name}' on table '{table.DbName}' references column '{column.Table?.DbName ?? "<unknown>"}.{column.DbName}', but index columns must belong to the table that stores the index.");

                    if (!table.Columns.Contains(column))
                        return CreateColumnIndexFailure(
                            table,
                            $"Index '{index.Name}' on table '{table.DbName}' references column '{column.DbName}', but that column is not registered on the table.");
                }

                var duplicateColumnGroup = index.Columns
                    .GroupBy(column => column)
                    .FirstOrDefault(group => group.Count() > 1);

                if (duplicateColumnGroup != null)
                {
                    return CreateColumnIndexFailure(
                        table,
                        $"Index '{index.Name}' on table '{table.DbName}' contains duplicate column reference '{duplicateColumnGroup.Key.DbName}'. Index columns must be unique within an index.");
                }

                if (index.Characteristic == IndexCharacteristic.PrimaryKey &&
                    !ColumnsMatch(index.Columns, table.PrimaryKeyColumns))
                {
                    var primaryKeyColumns = table.PrimaryKeyColumns.Select(column => column.DbName).ToJoinedString(", ");
                    return CreateColumnIndexFailure(
                        table,
                        $"Primary-key index '{index.Name}' on table '{table.DbName}' must match the table primary-key columns '{primaryKeyColumns}'.");
                }

                if (index.Characteristic == IndexCharacteristic.ForeignKey)
                {
                    var nonForeignKeyColumn = index.Columns.FirstOrDefault(column => !column.ForeignKey);
                    if (nonForeignKeyColumn != null)
                    {
                        return CreateColumnIndexFailure(
                            table,
                            $"Foreign-key index '{index.Name}' on table '{table.DbName}' references column '{nonForeignKeyColumn.DbName}', but foreign-key index columns must be marked as foreign keys.");
                    }
                }
            }

            var duplicateIndexGroup = table.ColumnIndices
                .Where(index => index is not null)
                .GroupBy(index => index.Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicateIndexGroup != null)
                return CreateColumnIndexFailure(
                    table,
                    $"Table '{table.DbName}' contains duplicate column index name '{duplicateIndexGroup.Key}'. Index names must be unique within a table.");
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
                        $"Column '{column.DbName}' is registered on table '{table.DbName}', but the column belongs to table '{column.Table?.DbName ?? "<unknown>"}'.");

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

                var columnAttributeFailure = ValidateValuePropertyColumnAttributes(property, column);
                if (columnAttributeFailure is not null)
                    return columnAttributeFailure;
            }
        }

        return true;
    }

    private static IDLOptionFailure? ValidateValuePropertyColumnAttributes(ValueProperty property, ColumnDefinition column)
    {
        if (!TryGetSingleAttribute<ColumnAttribute>(
                property.Attributes,
                out var columnAttribute,
                out var duplicateColumnAttribute))
        {
            return CreateValuePropertyAttributeFailure(
                property,
                duplicateColumnAttribute!,
                $"Value property '{GetValuePropertyDisplayName(property)}' has multiple [Column] attributes. A value property can identify only one database column.");
        }

        if (columnAttribute is not null)
        {
            if (string.IsNullOrWhiteSpace(columnAttribute.Name))
                return CreateValuePropertyAttributeFailure(
                    property,
                    columnAttribute,
                    $"Value property '{GetValuePropertyDisplayName(property)}' has an empty [Column] attribute name.");

            if (!string.Equals(columnAttribute.Name, column.DbName, StringComparison.Ordinal))
                return CreateValuePropertyAttributeFailure(
                    property,
                    columnAttribute,
                    $"Value property '{GetValuePropertyDisplayName(property)}' has [Column] name '{columnAttribute.Name}', but it is linked to column '{column.Table.DbName}.{column.DbName}'. Value-property column attributes must match the linked column metadata.");
        }

        var primaryKeyFailure = ValidateSingleColumnFlagAttribute<PrimaryKeyAttribute>(
            property,
            column,
            column.PrimaryKey,
            "PrimaryKey",
            "a primary key");
        if (primaryKeyFailure is not null)
            return primaryKeyFailure;

        var nullableFailure = ValidateSingleColumnFlagAttribute<NullableAttribute>(
            property,
            column,
            column.Nullable,
            "Nullable",
            "nullable");
        if (nullableFailure is not null)
            return nullableFailure;

        var autoIncrementFailure = ValidateSingleColumnFlagAttribute<AutoIncrementAttribute>(
            property,
            column,
            column.AutoIncrement,
            "AutoIncrement",
            "auto-increment");
        if (autoIncrementFailure is not null)
            return autoIncrementFailure;

        var foreignKeyAttribute = FirstAttributeOrDefault<ForeignKeyAttribute>(property.Attributes);
        if (foreignKeyAttribute is not null && !column.ForeignKey)
            return CreateValuePropertyAttributeFailure(
                property,
                foreignKeyAttribute,
                $"Value property '{GetValuePropertyDisplayName(property)}' has [ForeignKey] metadata, but linked column '{column.Table.DbName}.{column.DbName}' is not marked as a foreign key.");

        if (column.ForeignKey && foreignKeyAttribute is null)
            return CreateValuePropertyFailure(
                property,
                $"Column '{column.Table.DbName}.{column.DbName}' is marked as a foreign key, but value property '{GetValuePropertyDisplayName(property)}' has no [ForeignKey] attribute.");

        return ValidateValuePropertyTypeAttributes(property, column);
    }

    private static IDLOptionFailure? ValidateSingleColumnFlagAttribute<TAttribute>(
        ValueProperty property,
        ColumnDefinition column,
        bool columnValue,
        string attributeName,
        string metadataName)
        where TAttribute : Attribute
    {
        if (!TryGetSingleAttribute<TAttribute>(
                property.Attributes,
                out var attribute,
                out var duplicateAttribute))
        {
            return CreateValuePropertyAttributeFailure(
                property,
                duplicateAttribute!,
                $"Value property '{GetValuePropertyDisplayName(property)}' has multiple [{attributeName}] attributes.");
        }

        if (attribute is not null && !columnValue)
            return CreateValuePropertyAttributeFailure(
                property,
                attribute,
                $"Value property '{GetValuePropertyDisplayName(property)}' has [{attributeName}], but linked column '{column.Table.DbName}.{column.DbName}' is not marked as {metadataName}.");

        return null;
    }

    private static IDLOptionFailure? ValidateValuePropertyTypeAttributes(ValueProperty property, ColumnDefinition column)
    {
        var attributes = property.Attributes;
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not TypeAttribute attribute)
                continue;

            for (var previousIndex = 0; previousIndex < i; previousIndex++)
            {
                if (attributes[previousIndex] is TypeAttribute previous &&
                    previous.DatabaseType == attribute.DatabaseType)
                {
                    return CreateValuePropertyAttributeFailure(
                        property,
                        attribute,
                        $"Value property '{GetValuePropertyDisplayName(property)}' has multiple [Type] attributes for database type '{attribute.DatabaseType}'. A value property can define only one type attribute per provider.");
                }
            }
        }

        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not TypeAttribute attribute)
                continue;

            var columnType = FindColumnType(column.DbTypes, attribute.DatabaseType);
            if (columnType is null)
                return CreateValuePropertyAttributeFailure(
                    property,
                    attribute,
                    $"Value property '{GetValuePropertyDisplayName(property)}' has [Type] metadata for database type '{attribute.DatabaseType}', but linked column '{column.Table.DbName}.{column.DbName}' has no matching database type metadata.");

            if (!TypeAttributeMatchesColumnType(attribute, columnType))
                return CreateValuePropertyAttributeFailure(
                    property,
                    attribute,
                    $"Value property '{GetValuePropertyDisplayName(property)}' has [Type] metadata '{FormatTypeAttribute(attribute)}', but linked column '{column.Table.DbName}.{column.DbName}' defines '{FormatDatabaseColumnType(columnType)}'. Value-property type attributes must match linked column metadata.");
        }

        return null;
    }

    private static DatabaseColumnType? FindColumnType(
        IReadOnlyList<DatabaseColumnType> columnTypes,
        DatabaseType databaseType)
    {
        for (var i = 0; i < columnTypes.Count; i++)
        {
            var columnType = columnTypes[i];
            if (columnType is not null && columnType.DatabaseType == databaseType)
                return columnType;
        }

        return null;
    }

    private static bool TryGetSingleAttribute<TAttribute>(
        IReadOnlyList<Attribute> attributes,
        out TAttribute? attribute,
        out TAttribute? duplicateAttribute)
        where TAttribute : Attribute
    {
        attribute = null;
        duplicateAttribute = null;

        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is not TAttribute current)
                continue;

            if (attribute is not null)
            {
                duplicateAttribute = current;
                return false;
            }

            attribute = current;
        }

        return true;
    }

    private static TAttribute? FirstAttributeOrDefault<TAttribute>(IReadOnlyList<Attribute> attributes)
        where TAttribute : Attribute
    {
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] is TAttribute attribute)
                return attribute;
        }

        return null;
    }

    private static bool TypeAttributeMatchesColumnType(TypeAttribute attribute, DatabaseColumnType columnType) =>
        attribute.DatabaseType == columnType.DatabaseType &&
        string.Equals(attribute.Name, columnType.Name, StringComparison.Ordinal) &&
        attribute.Length == columnType.Length &&
        attribute.Decimals == columnType.Decimals &&
        attribute.Signed == columnType.Signed;

    private static string FormatTypeAttribute(TypeAttribute attribute) =>
        $"{attribute.DatabaseType}:{attribute.Name}(length={FormatNullableValue(attribute.Length)}, decimals={FormatNullableValue(attribute.Decimals)}, signed={FormatNullableValue(attribute.Signed)})";

    private static string FormatDatabaseColumnType(DatabaseColumnType columnType) =>
        $"{columnType.DatabaseType}:{columnType.Name}(length={FormatNullableValue(columnType.Length)}, decimals={FormatNullableValue(columnType.Decimals)}, signed={FormatNullableValue(columnType.Signed)})";

    private static string FormatNullableValue<T>(T? value)
        where T : struct =>
        value?.ToString() ?? "<null>";

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
                var databaseTypes = new HashSet<DatabaseType>();

                foreach (var dbType in column.DbTypes)
                {
                    if (dbType is null)
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' contains a null database type.");

                    if (!IsValidProviderScopedDatabaseType(dbType.DatabaseType))
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' has database type '{dbType.Name}' with unsupported database type '{dbType.DatabaseType}'.");

                    if (string.IsNullOrWhiteSpace(dbType.Name))
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' has an empty database type name for database type '{dbType.DatabaseType}'.");

                    if (dbType.Decimals.HasValue && !dbType.Length.HasValue)
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' has database type '{dbType.Name}' with decimals, but no length. Database type decimals require a length.");

                    if (!databaseTypes.Add(dbType.DatabaseType))
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{table.DbName}.{column.DbName}' defines multiple database types for '{dbType.DatabaseType}'. A column can have only one database type per provider.");
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
                var enumAttributes = property.Attributes.OfType<EnumAttribute>().ToArray();
                if (enumAttributes.Length > 1)
                    return CreateValuePropertyFailure(
                        property,
                        $"Value property '{GetValuePropertyDisplayName(property)}' defines multiple EnumAttribute metadata entries. A value property can have only one enum attribute.");

                if (!property.EnumProperty.HasValue)
                {
                    if (enumAttributes.Length == 1)
                        return CreateValuePropertyFailure(
                            property,
                            $"Value property '{GetValuePropertyDisplayName(property)}' declares EnumAttribute metadata, but no enum property metadata is attached.");

                    if (property.CsType.Type?.IsEnum == true)
                        return CreateValuePropertyFailure(
                            property,
                            $"Value property '{GetValuePropertyDisplayName(property)}' has enum C# type '{property.CsType.Name}', but no enum metadata is attached.");

                    continue;
                }

                var enumProperty = property.EnumProperty.Value;
                var csValues = enumProperty.CsEnumValues;
                var explicitDbValues = enumProperty.DbEnumValues;
                var dbValues = explicitDbValues.Count != 0 ? explicitDbValues : csValues;

                if (!IsValidCSharpIdentifier(property.CsType.Name))
                    return CreateValuePropertyFailure(
                        property,
                        $"Enum value property '{GetValuePropertyDisplayName(property)}' uses C# enum type name '{property.CsType.Name}', which is not a valid unescaped C# identifier.");

                if (csValues.Count == 0 && explicitDbValues.Count == 0)
                    return CreateValuePropertyFailure(
                        property,
                        $"Enum value property '{GetValuePropertyDisplayName(property)}' must define at least one enum value.");

                foreach (var value in csValues)
                {
                    if (!IsValidCSharpIdentifier(value.name))
                        return CreateValuePropertyFailure(
                            property,
                            $"Enum value property '{GetValuePropertyDisplayName(property)}' has invalid C# enum member name '{value.name}'. Enum member names must be valid unescaped C# identifiers.");
                }

                var duplicateCsValue = csValues
                    .GroupBy(x => x.name, StringComparer.Ordinal)
                    .FirstOrDefault(x => x.Count() > 1);
                if (duplicateCsValue != null)
                    return CreateValuePropertyFailure(
                        property,
                        $"Enum value property '{GetValuePropertyDisplayName(property)}' defines duplicate C# enum member name '{duplicateCsValue.Key}'.");

                foreach (var value in dbValues)
                {
                    if (value.name is null)
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

        return IsValidCSharpIdentifier(value.AsSpan());
    }

    private static bool IsValidCSharpIdentifier(ReadOnlySpan<char> identifier)
    {
        if (identifier.Length == 0)
            return false;

        if (!IsCSharpIdentifierStart(identifier[0]))
            return false;

        if (IsCSharpKeyword(identifier))
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

        return new CSharpTypeReferenceNameParser(typeName).TryParse();
    }

    private static bool IsCSharpIdentifierStart(char character) =>
        character == '_' || char.IsLetter(character);

    private static bool IsCSharpIdentifierPart(char character) =>
        character == '_' || char.IsLetterOrDigit(character);

    private static bool IsCSharpKeyword(ReadOnlySpan<char> identifier) =>
        identifier switch
        {
            "abstract" or "as" or "base" or "bool" or "break" or "byte" or "case" or "catch" or
            "char" or "checked" or "class" or "const" or "continue" or "decimal" or "default" or
            "delegate" or "do" or "double" or "else" or "enum" or "event" or "explicit" or
            "extern" or "false" or "finally" or "fixed" or "float" or "for" or "foreach" or
            "goto" or "if" or "implicit" or "in" or "int" or "interface" or "internal" or
            "is" or "lock" or "long" or "namespace" or "new" or "null" or "object" or
            "operator" or "out" or "override" or "params" or "private" or "protected" or
            "public" or "readonly" or "ref" or "return" or "sbyte" or "sealed" or "short" or
            "sizeof" or "stackalloc" or "static" or "string" or "struct" or "switch" or
            "this" or "throw" or "true" or "try" or "typeof" or "uint" or "ulong" or
            "unchecked" or "unsafe" or "ushort" or "using" or "virtual" or "void" or
            "volatile" or "while" => true,
            _ => false
        };

    private struct CSharpTypeReferenceNameParser
    {
        private readonly string text;
        private int position;

        public CSharpTypeReferenceNameParser(string text)
        {
            this.text = text;
        }

        public bool TryParse()
        {
            if (!TryParseType())
                return false;

            SkipWhitespace();
            return position == text.Length;
        }

        private bool TryParseType()
        {
            SkipWhitespace();

            if (!TryParseQualifiedIdentifier())
                return false;

            SkipWhitespace();
            if (TryConsume('<'))
            {
                do
                {
                    if (!TryParseType())
                        return false;

                    SkipWhitespace();
                }
                while (TryConsume(','));

                if (!TryConsume('>'))
                    return false;
            }

            while (TryParseArrayRank())
            {
            }

            while (TryConsume('*'))
            {
            }

            TryConsume('?');
            SkipWhitespace();
            return true;
        }

        private bool TryParseQualifiedIdentifier()
        {
            if (!TryParseIdentifier())
                return false;

            if (TryConsume("::") && !TryParseIdentifier())
                return false;

            while (TryConsume('.'))
            {
                if (!TryParseIdentifier())
                    return false;
            }

            return true;
        }

        private bool TryParseIdentifier()
        {
            SkipWhitespace();

            if (position >= text.Length || !IsCSharpIdentifierStart(text[position]))
                return false;

            var start = position++;
            while (position < text.Length && IsCSharpIdentifierPart(text[position]))
                position++;

            var identifier = text.AsSpan(start, position - start);
            return !IsCSharpKeyword(identifier) || IsPredefinedTypeName(identifier);
        }

        private static bool IsPredefinedTypeName(ReadOnlySpan<char> identifier) =>
            identifier switch
            {
                "bool" or "byte" or "char" or "decimal" or "double" or "float" or
                "int" or "long" or "object" or "sbyte" or "short" or "string" or
                "uint" or "ulong" or "ushort" or "void" => true,
                _ => false
            };

        private bool TryParseArrayRank()
        {
            var start = position;
            if (!TryConsume('['))
                return false;

            while (TryConsume(','))
            {
            }

            if (TryConsume(']'))
                return true;

            position = start;
            return false;
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (position >= text.Length || text[position] != expected)
                return false;

            position++;
            return true;
        }

        private bool TryConsume(string expected)
        {
            SkipWhitespace();
            if (position + expected.Length > text.Length)
                return false;

            for (var i = 0; i < expected.Length; i++)
            {
                if (text[position + i] != expected[i])
                    return false;
            }

            position += expected.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
        }
    }

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
                    !IsValidProviderScopedDatabaseType(defaultSql.DatabaseType))
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

                if (property.RelationName != null && string.IsNullOrWhiteSpace(property.RelationName))
                    return CreateRelationPropertyFailure(
                        property,
                        null,
                        $"Relation property '{GetRelationPropertyDisplayName(property)}' has an empty relation name.");

                if (property.RelationPart?.ColumnIndex is { } relationIndex &&
                    !ReferenceEquals(relationIndex.Table, table))
                {
                    return CreateRelationPropertyFailure(
                        property,
                        null,
                        $"Relation property '{model.CsType.Name}.{property.PropertyName}' is linked to relation part on table '{relationIndex.Table.DbName}', but relation properties must point at a relation part on their own table '{table.DbName}'.");
                }

                if (property.RelationPart is { } relationPart)
                {
                    var relation = relationPart.Relation;
                    if (property.RelationName is not null &&
                        !string.IsNullOrWhiteSpace(relation.ConstraintName) &&
                        !string.Equals(property.RelationName, relation.ConstraintName, StringComparison.Ordinal))
                    {
                        return CreateRelationPropertyFailure(
                            property,
                            null,
                            $"Relation property '{model.CsType.Name}.{property.PropertyName}' stores relation name '{property.RelationName}', but it is linked to relation '{relation.ConstraintName}'.");
                    }

                    var relationAttribute = property.Attributes.OfType<RelationAttribute>().FirstOrDefault();
                    var otherPart = TryGetOtherRelationSide(relationPart);
                    if (relationAttribute is null && otherPart is not null)
                    {
                        return CreateRelationPropertyFailure(
                            property,
                            null,
                            $"Relation property '{model.CsType.Name}.{property.PropertyName}' is linked to relation '{relation.ConstraintName}', but has no [Relation] attribute.");
                    }

                    if (relationAttribute is not null && otherPart is not null)
                    {
                        if (!string.Equals(relationAttribute.Table, otherPart.ColumnIndex.Table.DbName, StringComparison.Ordinal))
                            return CreateRelationPropertyFailure(
                                property,
                                relationAttribute,
                                $"Relation property '{model.CsType.Name}.{property.PropertyName}' targets table '{relationAttribute.Table}', but it is linked to relation side on table '{otherPart.ColumnIndex.Table.DbName}'.");

                        if (!RelationAttributeColumnsMatch(relationAttribute.Columns, otherPart.ColumnIndex.Columns))
                            return CreateRelationPropertyFailure(
                                property,
                                relationAttribute,
                                $"Relation property '{model.CsType.Name}.{property.PropertyName}' targets columns '{relationAttribute.Columns.ToJoinedString(", ")}', but it is linked to relation side columns '{otherPart.ColumnIndex.Columns.Select(column => column.DbName).ToJoinedString(", ")}'.");

                        if (relationAttribute.Name is not null &&
                            !string.IsNullOrWhiteSpace(relation.ConstraintName) &&
                            !string.Equals(relationAttribute.Name, relation.ConstraintName, StringComparison.Ordinal))
                        {
                            return CreateRelationPropertyFailure(
                                property,
                                relationAttribute,
                                $"Relation property '{model.CsType.Name}.{property.PropertyName}' targets relation '{relationAttribute.Name}', but it is linked to relation '{relation.ConstraintName}'.");
                        }
                    }

                    if (otherPart is not null)
                    {
                        var expectedTypeName = GetExpectedRelationPropertyTypeName(relationPart, otherPart);
                        if (expectedTypeName is not null &&
                            !string.Equals(property.CsType.Name, expectedTypeName, StringComparison.Ordinal))
                        {
                            return CreateRelationPropertyFailure(
                                property,
                                null,
                                $"Relation property '{model.CsType.Name}.{property.PropertyName}' uses C# type '{property.CsType.Name}', but relation side on table '{otherPart.ColumnIndex.Table.DbName}' requires '{expectedTypeName}'.");
                        }
                    }
                }
            }
        }

        return true;
    }

    private static RelationPart? TryGetOtherRelationSide(RelationPart relationPart) =>
        relationPart.Type switch
        {
            RelationPartType.CandidateKey => relationPart.Relation.ForeignKey,
            RelationPartType.ForeignKey => relationPart.Relation.CandidateKey,
            _ => null
        };

    private static string? GetExpectedRelationPropertyTypeName(RelationPart relationPart, RelationPart otherPart) =>
        relationPart.Type switch
        {
            RelationPartType.ForeignKey => otherPart.ColumnIndex.Table.Model.CsType.Name,
            RelationPartType.CandidateKey => $"IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}>",
            _ => null
        };

    private static bool RelationAttributeColumnsMatch(IReadOnlyList<string> attributeColumns, IReadOnlyList<ColumnDefinition> relationColumns)
    {
        if (attributeColumns.Count != relationColumns.Count)
            return false;

        for (var i = 0; i < attributeColumns.Count; i++)
        {
            if (!string.Equals(attributeColumns[i], relationColumns[i].DbName, StringComparison.Ordinal))
                return false;
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
                    database,
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

                var registeredRelationParts = new HashSet<RelationPart>();
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
                        database,
                        relationPart,
                        $"index '{index.Name}' on table '{tableModel.Table.DbName}'",
                        message => CreateColumnIndexFailure(tableModel.Table, message));
                    if (failure != null)
                        return failure;

                    if (!registeredRelationParts.Add(relationPart))
                    {
                        var relationName = string.IsNullOrWhiteSpace(relationPart.Relation.ConstraintName)
                            ? "<unnamed>"
                            : relationPart.Relation.ConstraintName;
                        return CreateColumnIndexFailure(
                            tableModel.Table,
                            $"Index '{index.Name}' on table '{tableModel.Table.DbName}' contains duplicate relation part for relation '{relationName}'. Relation parts must be unique within an index.");
                    }
                }
            }
        }

        return true;
    }

    private static IDLOptionFailure? ValidateExistingRelationPart(
        DatabaseDefinition database,
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

        if (string.IsNullOrWhiteSpace(relation.ConstraintName))
            return createFailure($"Existing relation referenced by {ownerDescription} has an empty constraint name.");

        if (!Enum.IsDefined(typeof(RelationPartType), relationPart.Type))
            return createFailure($"Existing relation part referenced by {ownerDescription} has unsupported relation-part type '{relationPart.Type}'.");

        if (!Enum.IsDefined(typeof(RelationType), relation.Type))
            return createFailure($"Existing relation '{relationName}' referenced by {ownerDescription} uses unsupported relation type '{relation.Type}'.");

        if (!Enum.IsDefined(typeof(ReferentialAction), relation.OnUpdate))
            return createFailure($"Existing relation '{relationName}' referenced by {ownerDescription} uses unsupported on-update action '{relation.OnUpdate}'.");

        if (!Enum.IsDefined(typeof(ReferentialAction), relation.OnDelete))
            return createFailure($"Existing relation '{relationName}' referenced by {ownerDescription} uses unsupported on-delete action '{relation.OnDelete}'.");

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

        var foreignKeyIndexFailure = ValidateRelationPartIndexRegistration(
            database,
            relation.ForeignKey,
            relationName,
            "foreign-key",
            createFailure);
        if (foreignKeyIndexFailure != null)
            return foreignKeyIndexFailure;

        var candidateKeyIndexFailure = ValidateRelationPartIndexRegistration(
            database,
            relation.CandidateKey,
            relationName,
            "candidate-key",
            createFailure);
        if (candidateKeyIndexFailure != null)
            return candidateKeyIndexFailure;

        if (relation.ForeignKey.ColumnIndex.Characteristic != IndexCharacteristic.ForeignKey)
        {
            return createFailure(
                $"Existing relation '{relationName}' has a foreign-key part on index '{relation.ForeignKey.ColumnIndex.Name}', but that index is marked as '{relation.ForeignKey.ColumnIndex.Characteristic}' instead of '{IndexCharacteristic.ForeignKey}'.");
        }

        if (relation.CandidateKey.ColumnIndex.Characteristic is not IndexCharacteristic.PrimaryKey and not IndexCharacteristic.Unique)
        {
            return createFailure(
                $"Existing relation '{relationName}' has a candidate-key part on index '{relation.CandidateKey.ColumnIndex.Name}', but candidate-key indexes must be primary or unique.");
        }

        if (!relation.ForeignKey.ColumnIndex.RelationParts.Contains(relation.ForeignKey))
            return createFailure($"Existing relation '{relationName}' foreign-key part is not registered on index '{relation.ForeignKey.ColumnIndex.Name}'.");

        if (!relation.CandidateKey.ColumnIndex.RelationParts.Contains(relation.CandidateKey))
            return createFailure($"Existing relation '{relationName}' candidate-key part is not registered on index '{relation.CandidateKey.ColumnIndex.Name}'.");

        if (relation.ForeignKey.ColumnIndex.Columns.Count != relation.CandidateKey.ColumnIndex.Columns.Count)
        {
            return createFailure(
                $"Existing relation '{relationName}' has mismatched column counts between foreign-key index '{relation.ForeignKey.ColumnIndex.Name}' and candidate-key index '{relation.CandidateKey.ColumnIndex.Name}'.");
        }

        var foreignKeyAttributeFailure = ValidateExistingRelationForeignKeyAttributes(
            relation,
            relationName,
            createFailure);
        if (foreignKeyAttributeFailure != null)
            return foreignKeyAttributeFailure;

        return null;
    }

    private static IDLOptionFailure? ValidateExistingRelationForeignKeyAttributes(
        RelationDefinition relation,
        string relationName,
        Func<string, IDLOptionFailure> createFailure)
    {
        var foreignKeyColumns = relation.ForeignKey.ColumnIndex.Columns;
        var candidateColumns = relation.CandidateKey.ColumnIndex.Columns;
        var matchingAttributes = new List<(ColumnDefinition Column, ForeignKeyAttribute Attribute)>(foreignKeyColumns.Count);

        for (var i = 0; i < foreignKeyColumns.Count; i++)
        {
            var foreignKeyColumn = foreignKeyColumns[i];
            var candidateColumn = candidateColumns[i];
            var foreignKeyTarget = $"{candidateColumn.Table.DbName}.{candidateColumn.DbName}";
            if (foreignKeyColumn.ValueProperty is null)
                return createFailure(
                    $"Existing relation '{relationName}' foreign-key column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' has no value property for [ForeignKey] metadata.");

            var foreignKeyAttributes = foreignKeyColumn.ValueProperty.Attributes
                .OfType<ForeignKeyAttribute>()
                .Where(attribute => string.Equals(attribute.Name, relation.ConstraintName, StringComparison.Ordinal))
                .ToList();
            var matchingAttributesForTarget = foreignKeyAttributes
                .Where(attribute =>
                    string.Equals(attribute.Table, candidateColumn.Table.DbName, StringComparison.Ordinal) &&
                    string.Equals(attribute.Column, candidateColumn.DbName, StringComparison.Ordinal))
                .ToList();

            if (matchingAttributesForTarget.Count > 1)
            {
                return createFailure(
                    $"Existing relation '{relationName}' foreign-key column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' has duplicate [ForeignKey] attributes with constraint name '{relation.ConstraintName}' targeting '{foreignKeyTarget}'.");
            }

            var matchingAttribute = matchingAttributesForTarget.FirstOrDefault();
            if (matchingAttribute is not null && foreignKeyAttributes.Count != 1)
            {
                var targets = foreignKeyAttributes
                    .Select(attribute => $"{attribute.Table}.{attribute.Column}")
                    .ToJoinedString(", ");
                return createFailure(
                    $"Existing relation '{relationName}' foreign-key column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' has multiple [ForeignKey] attributes with constraint name '{relation.ConstraintName}'. Expected one targeting '{foreignKeyTarget}', but found '{targets}'.");
            }

            if (matchingAttribute is null)
            {
                if (foreignKeyAttributes.Count == 0)
                    return createFailure(
                        $"Existing relation '{relationName}' foreign-key column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' is missing a [ForeignKey] attribute with constraint name '{relation.ConstraintName}' targeting '{foreignKeyTarget}'.");

                var targets = foreignKeyAttributes
                    .Select(attribute => $"{attribute.Table}.{attribute.Column}")
                    .ToJoinedString(", ");
                return createFailure(
                    $"Existing relation '{relationName}' pairs foreign-key column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' with candidate column '{foreignKeyTarget}', but the column's [ForeignKey] metadata for that relation targets '{targets}'.");
            }

            if (matchingAttribute.OnUpdate != relation.OnUpdate ||
                matchingAttribute.OnDelete != relation.OnDelete)
            {
                return createFailure(
                    $"Existing relation '{relationName}' uses on-update action '{relation.OnUpdate}' and on-delete action '{relation.OnDelete}', but foreign-key column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' metadata uses on-update action '{matchingAttribute.OnUpdate}' and on-delete action '{matchingAttribute.OnDelete}'.");
            }

            matchingAttributes.Add((foreignKeyColumn, matchingAttribute));
        }

        var ordinalFailure = ValidateExistingCompositeForeignKeyOrdinals(
            relationName,
            matchingAttributes,
            createFailure);
        if (ordinalFailure != null)
            return ordinalFailure;

        return null;
    }

    private static IDLOptionFailure? ValidateExistingCompositeForeignKeyOrdinals(
        string relationName,
        IReadOnlyList<(ColumnDefinition Column, ForeignKeyAttribute Attribute)> matchingAttributes,
        Func<string, IDLOptionFailure> createFailure)
    {
        if (matchingAttributes.Count <= 1)
            return null;

        var ordinals = matchingAttributes
            .Select(x => x.Attribute.Ordinal)
            .ToArray();
        var ordinalCount = ordinals.Count(x => x.HasValue);

        if (ordinalCount == 0)
            return null;

        if (ordinalCount != ordinals.Length)
        {
            var missingColumns = matchingAttributes
                .Where(x => !x.Attribute.Ordinal.HasValue)
                .Select(x => $"{x.Column.Table.DbName}.{x.Column.DbName}")
                .ToJoinedString(", ");
            return createFailure(
                $"Existing relation '{relationName}' has partial composite [ForeignKey] ordinal metadata. Columns without ordinals: '{missingColumns}'.");
        }

        if (OrdinalsMatch(matchingAttributes, 0) || OrdinalsMatch(matchingAttributes, 1))
            return null;

        var foreignKeyOrder = matchingAttributes
            .Select(x => x.Column.DbName)
            .ToJoinedString(", ");
        var actualOrdinals = matchingAttributes
            .Select(x => $"{x.Column.Table.DbName}.{x.Column.DbName}={x.Attribute.Ordinal}")
            .ToJoinedString(", ");
        var zeroBasedExpected = Enumerable.Range(0, matchingAttributes.Count)
            .Select(x => x.ToString())
            .ToJoinedString(", ");
        var oneBasedExpected = Enumerable.Range(1, matchingAttributes.Count)
            .Select(x => x.ToString())
            .ToJoinedString(", ");

        return createFailure(
            $"Existing relation '{relationName}' has composite [ForeignKey] ordinals that disagree with foreign-key column order '{foreignKeyOrder}'. Expected contiguous zero-based ordinals '{zeroBasedExpected}' or one-based ordinals '{oneBasedExpected}', but found '{actualOrdinals}'.");
    }

    private static bool OrdinalsMatch(
        IReadOnlyList<(ColumnDefinition Column, ForeignKeyAttribute Attribute)> matchingAttributes,
        int start)
    {
        for (var i = 0; i < matchingAttributes.Count; i++)
        {
            if (matchingAttributes[i].Attribute.Ordinal != start + i)
                return false;
        }

        return true;
    }

    private static IDLOptionFailure? ValidateRelationPartIndexRegistration(
        DatabaseDefinition database,
        RelationPart relationPart,
        string relationName,
        string sideName,
        Func<string, IDLOptionFailure> createFailure)
    {
        var table = relationPart.ColumnIndex.Table;
        var tableName = table?.DbName ?? "<unknown>";

        if (table is null)
            return createFailure(
                $"Existing relation '{relationName}' has a {sideName} part on table '{tableName}', but that table is not registered on database '{database.DbName}'.");

        if (!database.TableModels.Any(tableModel => ReferenceEquals(tableModel?.Table, table)))
            return createFailure(
                $"Existing relation '{relationName}' has a {sideName} part on table '{tableName}', but that table is not registered on database '{database.DbName}'.");

        if (!table.ColumnIndices.Contains(relationPart.ColumnIndex))
            return createFailure(
                $"Existing relation '{relationName}' has a {sideName} part on index '{relationPart.ColumnIndex.Name}', but that index is not registered on table '{tableName}'.");

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

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static Option<bool, IDLOptionFailure> ParseRelations(DatabaseDefinition database)
    {
        return ParseRelationsCore(database);
    }

    internal static Option<bool, IDLOptionFailure> ParseRelationsCore(DatabaseDefinition database)
    {
        return ParseRelationsCore(database, out _);
    }

    internal static Option<bool, IDLOptionFailure> ParseRelationsCore(
        DatabaseDefinition database,
        out bool generatedRelationProperties)
    {
        generatedRelationProperties = false;
        var foreignKeys = new List<ForeignKeyColumn>();
        var failures = new List<IDLOptionFailure>();
        foreach (var tableModel in database.TableModels)
        {
            if (tableModel.IsStub || tableModel.Table.Type != TableType.Table)
                continue;

            var table = tableModel.Table;
            var primaryKeyColumns = new List<ColumnDefinition>();
            foreach (var column in table.Columns)
            {
                if (column.PrimaryKey)
                    primaryKeyColumns.Add(column);

                if (!column.ForeignKey)
                    continue;

                foreach (var attribute in column.ValueProperty.Attributes)
                {
                    if (attribute is ForeignKeyAttribute foreignKey)
                        foreignKeys.Add(new ForeignKeyColumn(column, foreignKey));
                }
            }

            if (primaryKeyColumns.Count == 0)
            {
                failures.Add(CreateMissingPrimaryKeyFailure(table));
                continue;
            }

            if (!HasColumnIndex(table, IndexCharacteristic.PrimaryKey))
                table.ColumnIndices.AddCore(new ColumnIndex($"{table.DbName}_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, primaryKeyColumns));
        }

        for (var groupStart = 0; groupStart < foreignKeys.Count; groupStart++)
        {
            var firstGroupItem = foreignKeys[groupStart];
            if (HasEarlierForeignKeyGroup(foreignKeys, groupStart, firstGroupItem))
                continue;

            var orderedForeignKeys = new List<ForeignKeyColumn>();
            for (var i = groupStart; i < foreignKeys.Count; i++)
            {
                var foreignKey = foreignKeys[i];
                if (ForeignKeyGroupsMatch(firstGroupItem, foreignKey))
                    orderedForeignKeys.Add(foreignKey);
            }

            orderedForeignKeys.Sort(static (left, right) =>
            {
                var ordinalComparison = (left.Attribute.Ordinal ?? left.Column.Index)
                    .CompareTo(right.Attribute.Ordinal ?? right.Column.Index);
                return ordinalComparison != 0
                    ? ordinalComparison
                    : left.Column.Index.CompareTo(right.Column.Index);
            });

            var firstForeignKey = orderedForeignKeys[0];
            var firstAttribute = firstForeignKey.Attribute;
            var foreignKeyTable = firstForeignKey.Column.Table;
            if (!database.TryGetTableModel(firstAttribute.Table, out var candidateTableModel))
            {
                failures.Add(CreateForeignKeyFailure(
                    firstForeignKey.Column,
                    firstAttribute,
                    $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' references table '{firstAttribute.Table}', but no matching table exists in database '{database.DbName}'."));
                continue;
            }

            var candidateColumns = new List<ColumnDefinition>();
            var hasCandidateColumnFailures = false;
            foreach (var foreignKey in orderedForeignKeys)
            {
                var foreignKeyColumn = foreignKey.Column;
                var attribute = foreignKey.Attribute;
                if (!candidateTableModel.Table.TryGetColumnByDbName(attribute.Column, out var candidateColumn))
                {
                    failures.Add(CreateForeignKeyFailure(
                        foreignKeyColumn,
                        attribute,
                        $"Foreign key '{attribute.Name}' on column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' references column '{attribute.Table}.{attribute.Column}', but that column does not exist."));
                    hasCandidateColumnFailures = true;
                    continue;
                }

                candidateColumns.Add(candidateColumn);
            }

            if (hasCandidateColumnFailures)
                continue;

            var foreignKeyColumns = orderedForeignKeys.Select(x => x.Column).ToList();
            var manySideModel = foreignKeyTable.Model;
            var oneSideModel = candidateTableModel.Model;

            var foreignKeyIndex = foreignKeyTable.ColumnIndices.FirstOrDefault(x =>
                x.Characteristic == IndexCharacteristic.ForeignKey &&
                x.Name == firstAttribute.Name &&
                ColumnsMatch(x.Columns, foreignKeyColumns));
            var shouldAddForeignKeyIndex = false;
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
                    failures.Add(CreateForeignKeyFailure(
                        firstForeignKey.Column,
                        firstAttribute,
                        $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' could not create its index: {foreignKeyIndexFailure}"));
                    continue;
                }

                shouldAddForeignKeyIndex = true;
            }

            var candidateKeyIndex = FindCandidateKeyIndex(candidateTableModel.Table, candidateColumns);
            if (candidateKeyIndex == null)
            {
                failures.Add(CreateForeignKeyFailure(
                    firstForeignKey.Column,
                    firstAttribute,
                    $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' references columns '{candidateColumns.Select(x => x.DbName).ToJoinedString(", ")}' on table '{candidateTableModel.Table.DbName}', but no matching primary or unique key exists."));
                continue;
            }

            var relationFailures = new List<IDLOptionFailure>();
            var manyRelationPropertyResolved = TryGetRelationProperty(
                manySideModel,
                oneSideModel.Table.DbName,
                candidateColumns,
                firstAttribute.Name,
                out var manyToOneProp,
                out var manyToOnePropertyFailure);
            if (!manyRelationPropertyResolved)
            {
                relationFailures.Add(manyToOnePropertyFailure);
            }

            var manyToOnePropName = manyRelationPropertyResolved && manyToOneProp is null
                ? GetForeignKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute)
                : null;
            if (manyToOnePropName != null)
                manyToOnePropName = ResolveGeneratedRelationPropertyName(manySideModel, manyToOnePropName);

            var oneRelationPropertyResolved = TryGetRelationProperty(
                oneSideModel,
                manySideModel.Table.DbName,
                foreignKeyColumns,
                firstAttribute.Name,
                out var oneToManyProp,
                out var oneToManyPropertyFailure);
            if (!oneRelationPropertyResolved)
            {
                relationFailures.Add(oneToManyPropertyFailure);
            }

            var oneToManyPropName = oneRelationPropertyResolved && oneToManyProp is null
                ? GetCandidateKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute)
                : null;
            if (oneToManyPropName != null)
                oneToManyPropName = ResolveGeneratedRelationPropertyName(oneSideModel, oneToManyPropName);

            if (relationFailures.Count > 0)
            {
                failures.AddRange(relationFailures);
                continue;
            }

            if (shouldAddForeignKeyIndex)
                foreignKeyTable.ColumnIndices.AddCore(foreignKeyIndex);

            var relation = new RelationDefinition(firstAttribute.Name, RelationType.OneToMany);
            relation.SetOnUpdateCore(firstAttribute.OnUpdate);
            relation.SetOnDeleteCore(firstAttribute.OnDelete);
            var manySidePart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "");
            var oneSidePart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, "");
            relation.SetForeignKeyCore(manySidePart);
            relation.SetCandidateKeyCore(oneSidePart);

            // --- Link or Create Many-to-One Property ---
            if (manyToOneProp != null)
            {
                manyToOneProp.SetRelationPartCore(manySidePart);
                if (!manySidePart.ColumnIndex.RelationParts.Contains(manySidePart))
                    manySidePart.ColumnIndex.RelationParts.AddCore(manySidePart);
            }
            else
            {
                var propType = oneSideModel.CsType;
                var propAttr = new RelationAttribute(oneSideModel.Table.DbName, candidateColumns.Select(x => x.DbName).ToArray(), firstAttribute.Name);
                AddRelationPropertyCore(manySideModel, manyToOnePropName!, propType, manySidePart, propAttr);
                generatedRelationProperties = true;
            }

            // --- Link or Create One-to-Many Property ---
            if (oneToManyProp != null)
            {
                oneToManyProp.SetRelationPartCore(oneSidePart);
                if (!oneSidePart.ColumnIndex.RelationParts.Contains(oneSidePart))
                    oneSidePart.ColumnIndex.RelationParts.AddCore(oneSidePart);
            }
            else
            {
                var genericTypeName = manySideModel.CsType.Name;
                var propType = new CsTypeDeclaration($"IImmutableRelation<{genericTypeName}>", "DataLinq.Instances", ModelCsType.Interface);
                var propAttr = new RelationAttribute(manySideModel.Table.DbName, foreignKeyColumns.Select(x => x.DbName).ToArray(), firstAttribute.Name);
                AddRelationPropertyCore(oneSideModel, oneToManyPropName!, propType, oneSidePart, propAttr);
                generatedRelationProperties = true;
            }
        }

        if (failures.Count > 0)
            return SingleOrAggregate(failures);

        return ValidateResolvedRelationProperties(database);
    }

    private static IDLOptionFailure SingleOrAggregate(IReadOnlyCollection<IDLOptionFailure> failures) =>
        failures.Count == 1
            ? failures.First()
            : DLOptionFailure.AggregateFail(failures);

    private static bool HasColumnIndex(TableDefinition table, IndexCharacteristic characteristic)
    {
        foreach (var index in table.ColumnIndices)
        {
            if (index.Characteristic == characteristic)
                return true;
        }

        return false;
    }

    private static bool HasEarlierForeignKeyGroup(
        IReadOnlyList<ForeignKeyColumn> foreignKeys,
        int currentIndex,
        ForeignKeyColumn current)
    {
        for (var i = 0; i < currentIndex; i++)
        {
            if (ForeignKeyGroupsMatch(foreignKeys[i], current))
                return true;
        }

        return false;
    }

    private static bool ForeignKeyGroupsMatch(ForeignKeyColumn left, ForeignKeyColumn right) =>
        ReferenceEquals(left.Column.Table, right.Column.Table) &&
        string.Equals(left.Attribute.Name, right.Attribute.Name, StringComparison.Ordinal) &&
        string.Equals(left.Attribute.Table, right.Attribute.Table, StringComparison.Ordinal);

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

    private readonly struct ForeignKeyColumn
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
        IReadOnlyList<ColumnDefinition> referencedColumns,
        string constraintName,
        out RelationProperty? property,
        out IDLOptionFailure failure)
    {
        property = null;
        RelationAttribute? duplicateAttribute = null;
        RelationProperty? duplicateProperty = null;

        foreach (var candidate in model.RelationProperties.Values)
        {
            foreach (var attribute in candidate.Attributes)
            {
                if (attribute is not RelationAttribute relationAttribute ||
                    !RelationAttributeMatches(relationAttribute, referencedTableName, referencedColumns, constraintName))
                    continue;

                if (property is null)
                {
                    property = candidate;
                    break;
                }

                duplicateProperty = candidate;
                duplicateAttribute = relationAttribute;
                break;
            }

            if (duplicateProperty is not null)
                break;
        }

        if (duplicateProperty is null)
        {
            failure = null!;
            return true;
        }

        var target = $"{referencedTableName}.({FormatColumnNames(referencedColumns)})";
        var propertyNames = $"{property!.PropertyName}, {duplicateProperty.PropertyName}";
        var message = $"Multiple relation properties on model '{model.CsType.Name}' match relation target '{target}' for constraint '{constraintName}': {propertyNames}. Relation attributes must identify at most one property for a database relation.";

        property = null;
        failure = CreateRelationPropertyFailure(duplicateProperty, duplicateAttribute, message);
        return false;
    }

    private static bool RelationAttributeMatches(
        RelationAttribute attribute,
        string referencedTableName,
        IReadOnlyList<ColumnDefinition> referencedColumns,
        string constraintName) =>
        attribute.Table == referencedTableName &&
        RelationAttributeColumnsMatch(attribute.Columns, referencedColumns) &&
        (attribute.Name == null || attribute.Name == constraintName);

    private static string FormatColumnNames(IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 0)
            return string.Empty;

        var names = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++)
            names[i] = columns[i].DbName;

        return names.ToJoinedString(", ");
    }

    public static bool TryGetGeneratedRelationPropertyFallback(
        RelationProperty relationProperty,
        out string preferredPropertyName,
        out string existingPropertyKind)
    {
        preferredPropertyName = string.Empty;
        existingPropertyKind = string.Empty;

        if (!TryGetGeneratedRelationPropertyName(relationProperty, out preferredPropertyName))
            return false;

        if (string.Equals(relationProperty.PropertyName, preferredPropertyName, StringComparison.Ordinal))
            return false;

        var existingKind = GetExistingPropertyKind(relationProperty.Model, preferredPropertyName, relationProperty);
        if (existingKind is null)
            return false;

        var fallbackPropertyName = GetAvailableGeneratedRelationPropertyName(
            relationProperty.Model,
            preferredPropertyName,
            relationProperty);
        if (!string.Equals(relationProperty.PropertyName, fallbackPropertyName, StringComparison.Ordinal))
            return false;

        existingPropertyKind = existingKind;
        return true;
    }

    private static string ResolveGeneratedRelationPropertyName(ModelDefinition model, string propertyName)
    {
        return GetExistingPropertyKind(model, propertyName) is null
            ? propertyName
            : GetAvailableGeneratedRelationPropertyName(model, propertyName);
    }

    private static bool TryGetGeneratedRelationPropertyName(RelationProperty relationProperty, out string propertyName)
    {
        propertyName = string.Empty;

        if (relationProperty.RelationPart is null)
            return false;

        var relation = relationProperty.RelationPart.Relation;
        var foreignKeyPart = relation.ForeignKey;
        var candidateKeyPart = relation.CandidateKey;
        var foreignKeyColumns = foreignKeyPart.ColumnIndex.Columns;
        if (foreignKeyColumns.Count == 0)
            return false;

        if (!TryGetFirstForeignKeyAttribute(relation, out var firstAttribute))
            return false;

        var manySideModel = foreignKeyPart.ColumnIndex.Table.Model;
        var oneSideModel = candidateKeyPart.ColumnIndex.Table.Model;
        propertyName = relationProperty.RelationPart.Type == RelationPartType.ForeignKey
            ? GetForeignKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute)
            : GetCandidateKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute);
        return true;
    }

    private static bool TryGetFirstForeignKeyAttribute(RelationDefinition relation, out ForeignKeyAttribute attribute)
    {
        var foreignKeyColumns = relation.ForeignKey.ColumnIndex.Columns;
        var candidateColumns = relation.CandidateKey.ColumnIndex.Columns;
        for (var i = 0; i < foreignKeyColumns.Count; i++)
        {
            var candidateColumn = i < candidateColumns.Count ? candidateColumns[i] : null;
            foreach (var foreignKeyAttribute in foreignKeyColumns[i].ValueProperty.Attributes.OfType<ForeignKeyAttribute>())
            {
                if (ForeignKeyAttributeMatchesRelation(foreignKeyAttribute, relation, candidateColumn))
                {
                    attribute = foreignKeyAttribute;
                    return true;
                }
            }
        }

        foreach (var foreignKeyAttribute in foreignKeyColumns.SelectMany(static column => column.ValueProperty.Attributes.OfType<ForeignKeyAttribute>()))
        {
            if (string.Equals(foreignKeyAttribute.Name, relation.ConstraintName, StringComparison.Ordinal))
            {
                attribute = foreignKeyAttribute;
                return true;
            }
        }

        attribute = null!;
        return false;
    }

    private static bool ForeignKeyAttributeMatchesRelation(
        ForeignKeyAttribute attribute,
        RelationDefinition relation,
        ColumnDefinition? candidateColumn)
    {
        return candidateColumn is not null &&
               string.Equals(attribute.Name, relation.ConstraintName, StringComparison.Ordinal) &&
               string.Equals(attribute.Table, candidateColumn.Table.DbName, StringComparison.Ordinal) &&
               string.Equals(attribute.Column, candidateColumn.DbName, StringComparison.Ordinal);
    }

    private static string GetAvailableGeneratedRelationPropertyName(
        ModelDefinition model,
        string propertyName,
        RelationProperty? ignoredRelationProperty = null)
    {
        var fallbackName = $"{propertyName}Relation";
        if (!ModelDefinesProperty(model, fallbackName, ignoredRelationProperty))
            return fallbackName;

        for (var suffix = 2; ; suffix++)
        {
            var candidateName = $"{fallbackName}{suffix}";
            if (!ModelDefinesProperty(model, candidateName, ignoredRelationProperty))
                return candidateName;
        }
    }

    private static bool ModelDefinesProperty(
        ModelDefinition model,
        string propertyName,
        RelationProperty? ignoredRelationProperty = null)
    {
        return model.ValueProperties.ContainsKey(propertyName) ||
               model.RelationProperties.TryGetValue(propertyName, out var relationProperty) &&
               !ReferenceEquals(relationProperty, ignoredRelationProperty);
    }

    private static string? GetExistingPropertyKind(
        ModelDefinition model,
        string propertyName,
        RelationProperty? ignoredRelationProperty = null)
    {
        if (model.ValueProperties.ContainsKey(propertyName))
            return "value property";

        if (model.RelationProperties.TryGetValue(propertyName, out var relationProperty) &&
            !ReferenceEquals(relationProperty, ignoredRelationProperty))
            return "relation property";

        return null;
    }

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

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static void AddRelationProperty(ModelDefinition model, string propertyName, CsTypeDeclaration propertyType, RelationPart relationPart, RelationAttribute relationAttribute)
    {
        AddRelationPropertyCore(model, propertyName, propertyType, relationPart, relationAttribute);
    }

    internal static void AddRelationPropertyCore(ModelDefinition model, string propertyName, CsTypeDeclaration propertyType, RelationPart relationPart, RelationAttribute relationAttribute)
    {
        var originalPropertyName = propertyName;
        var i = 2;
        while (model.RelationProperties.ContainsKey(propertyName) || model.ValueProperties.ContainsKey(propertyName))
        {
            propertyName = $"{originalPropertyName}_{i++}";
        }

        var relationProperty = new RelationProperty(propertyName, propertyType, model, [relationAttribute]);
        relationProperty.SetRelationNameCore(relationAttribute.Name);
        relationProperty.SetRelationPartCore(relationPart); // Directly link the part
        if (relationPart.Type == RelationPartType.ForeignKey && relationPart.ColumnIndex.Columns.Any(x => x.Nullable))
            relationProperty.SetCsNullableCore();

        model.AddPropertyCore(relationProperty);

        // Also ensure the back-reference on the index is set
        if (!relationPart.ColumnIndex.RelationParts.Contains(relationPart))
        {
            relationPart.ColumnIndex.RelationParts.AddCore(relationPart);
        }
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static ValueProperty AttachValueProperty(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        return AttachValuePropertyCore(column, csTypeName, capitaliseNames);
    }

    internal static ValueProperty AttachValuePropertyCore(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        if (!TryAttachValuePropertyCore(column, csTypeName, capitaliseNames).TryUnwrap(out var property, out var failure))
            throw new InvalidOperationException(failure.ToString());

        return property;
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static Option<ValueProperty, IDLOptionFailure> TryAttachValueProperty(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        return TryAttachValuePropertyCore(column, csTypeName, capitaliseNames);
    }

    internal static Option<ValueProperty, IDLOptionFailure> TryAttachValuePropertyCore(ColumnDefinition column, string csTypeName, bool capitaliseNames)
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
        property.SetCsSizeCore(MetadataTypeConverter.CsTypeSize(csTypeName));
        property.SetCsNullableCore(column.Nullable || column.AutoIncrement);
        //property.SetAttributesCore(GetAttributes(property));
        property.SetColumnCore(column);

        column.SetValuePropertyCore(property);
        column.Table.Model.AddPropertyCore(column.ValueProperty);

        return property;
    }

    //public static void AttachEnumProperty(ValueProperty property, IEnumerable<(string name, int value)> enumValues, bool declaredInClass)
    //{
    //    property.SetEnumPropertyCore(new EnumProperty(enumValues, enumValues, declaredInClass));
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

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static void ParseAttributes(this DatabaseDefinition database)
    {
        database.ParseAttributesCore();
    }

    internal static void ParseAttributesCore(this DatabaseDefinition database)
    {
        foreach (var attribute in database.Attributes)
        {
            if (attribute is DatabaseAttribute databaseAttribute)
            {
                database.SetNameCore(databaseAttribute.Name);
                database.SetDbNameCore(databaseAttribute.Name);
            }

            if (attribute is UseCacheAttribute useCache)
                database.SetCacheCore(useCache.UseCache);

            if (attribute is CacheLimitAttribute cacheLimit)
                database.CacheLimits.AddCore((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                database.IndexCache.AddCore((indexCache.Type, indexCache.Amount));

            if (attribute is CacheCleanupAttribute cacheCleanup)
                database.CacheCleanup.AddCore((cacheCleanup.LimitType, cacheCleanup.Amount));
        }
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public static ColumnDefinition ParseColumn(this TableDefinition table, ValueProperty property)
    {
        return table.ParseColumnCore(property);
    }

    internal static ColumnDefinition ParseColumnCore(this TableDefinition table, ValueProperty property)
    {
        var column = new ColumnDefinition(property.PropertyName, table);
        column.SetValuePropertyCore(property);

        foreach (var attribute in property.Attributes)
        {
            if (attribute is ColumnAttribute columnAttribute)
                column.SetDbNameCore(columnAttribute.Name);

            if (attribute is NullableAttribute)
                column.SetNullableCore();

            //if (attribute is DefaultAttribute defaultAttribute)
            //    column.AddDefaultValue(defaultAttribute.Value);

            if (attribute is AutoIncrementAttribute)
                column.SetAutoIncrementCore();

            if (attribute is PrimaryKeyAttribute)
                column.SetPrimaryKeyCore();

            if (attribute is ForeignKeyAttribute)
                column.SetForeignKeyCore();

            if (attribute is TypeAttribute t)
                column.AddDbTypeCore(new DatabaseColumnType(t.DatabaseType, t.Name, t.Length, t.Decimals, t.Signed));
        }

        return column;
    }
}
