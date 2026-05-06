using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Instances;
using DataLinq.Interfaces;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Metadata;

public static class MetadataFromTypeFactory
{
    private const string GeneratedDatabaseModelMethodName = "GetDataLinqGeneratedModel";

    public static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromDatabaseModel<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TDatabase>()
        where TDatabase : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<TDatabase> =>
        DLOptionFailure.CatchAll(() => ParseDatabaseFromGeneratedModel(typeof(TDatabase), TDatabase.GetDataLinqGeneratedModel()));

    public static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromDatabaseModel(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicProperties)]
        Type type) => DLOptionFailure.CatchAll(() =>
        GetGeneratedDatabaseModel(type)
            .FlatMap(generatedModel => ParseDatabaseFromGeneratedModel(type, generatedModel)));

    private static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromGeneratedModel(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicProperties)]
        Type type,
        GeneratedDatabaseModelDeclaration generatedModel)
    {
        if (!generatedModel.TryValidate(type).TryUnwrap(out _, out var validationFailure))
            return validationFailure;

        var attributes = type.GetCustomAttributes(false).Cast<Attribute>().ToArray();
        var databaseName = attributes
            .OfType<DatabaseAttribute>()
            .LastOrDefault()?
            .Name ?? type.Name;

        var database = new MetadataDatabaseDraft(databaseName, new CsTypeDeclaration(type))
        {
            DbName = databaseName,
            Attributes = attributes,
            UseCache = attributes
                .OfType<UseCacheAttribute>()
                .LastOrDefault()?
                .UseCache ?? false,
            CacheLimits = attributes
                .OfType<CacheLimitAttribute>()
                .Select(x => (x.LimitType, x.Amount))
                .ToArray(),
            CacheCleanup = attributes
                .OfType<CacheCleanupAttribute>()
                .Select(x => (x.LimitType, x.Amount))
                .ToArray(),
            IndexCache = attributes
                .OfType<IndexCacheAttribute>()
                .Select(x => (x.Type, x.Amount))
                .ToArray(),
            TableModels = generatedModel.TableModels
                .Select(ParseTableModelDraft)
                .ToArray()
        };

        return new MetadataDefinitionFactory()
            .Build(database);
    }

    private static MetadataTableModelDraft ParseTableModelDraft(GeneratedTableModelDeclaration declaration)
    {
        var model = declaration.ModelType.ParseModelDraft(declaration);
        var table = ParseTableDraft(model, declaration.TableType);
        return new MetadataTableModelDraft(declaration.CsPropertyName, model, table);
    }

    private static MetadataTableDraft ParseTableDraft(MetadataModelDraft model, TableType tableType)
    {
        var dbName = model.CsType.Name;
        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                dbName = tableAttribute.Name;

            if (attribute is ViewAttribute viewAttribute)
                dbName = viewAttribute.Name;
        }

        return new MetadataTableDraft(dbName)
        {
            Type = tableType,
            Definition = model.Attributes
                .OfType<DefinitionAttribute>()
                .LastOrDefault()?
                .Sql,
            UseCache = model.Attributes
                .OfType<UseCacheAttribute>()
                .LastOrDefault()?
                .UseCache,
            CacheLimits = model.Attributes
                .OfType<CacheLimitAttribute>()
                .Select(x => (x.LimitType, x.Amount))
                .ToArray(),
            IndexCache = model.Attributes
                .OfType<IndexCacheAttribute>()
                .Select(x => (x.Type, x.Amount))
                .ToArray()
        };
    }

    private static Option<GeneratedDatabaseModelDeclaration, IDLOptionFailure> GetGeneratedDatabaseModel(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type databaseType)
    {
        if (databaseType is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Database type cannot be null.");

        var generatedModelMethod = databaseType.GetMethod(
            GeneratedDatabaseModelMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (generatedModelMethod is not null)
        {
            if (generatedModelMethod.ReturnType != typeof(GeneratedDatabaseModelDeclaration))
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Generated metadata bootstrap method '{databaseType.FullName}.{GeneratedDatabaseModelMethodName}' must return '{typeof(GeneratedDatabaseModelDeclaration).FullName}'.");

            return (GeneratedDatabaseModelDeclaration)generatedModelMethod.Invoke(null, null)!;
        }

        return DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"Database model '{databaseType.FullName}' is missing the generated DataLinq metadata hook '{GeneratedDatabaseModelMethodName}'. " +
            "Run the DataLinq source generator and ensure the database model is declared as a partial class.");
    }

    private static MetadataModelDraft ParseModelDraft(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.NonPublicProperties |
            DynamicallyAccessedMemberTypes.Interfaces)]
        this Type type,
        GeneratedTableModelDeclaration generatedDeclaration)
    {
        var attributes = type.GetCustomAttributes(false).Cast<Attribute>().ToArray();

        var interfaces = type.GetInterfaces();
        var modelInstanceInterfaceType = interfaces.Any(IsModelInstanceContract)
            ? interfaces.FirstOrDefault(IsApplicationInterface)
            : null;

        var propertyDrafts = type
            .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(ParsePropertyDraft)
            .OfType<object>()
            .ToList();

        var valueProperties = propertyDrafts
            .OfType<MetadataValuePropertyDraft>()
            .ToArray();
        var relationProperties = propertyDrafts
            .OfType<MetadataRelationPropertyDraft>()
            .ToArray();
        var usings = valueProperties
            .Select(x => x.CsType.Namespace)
            .Distinct()
            .Where(x => x != null)
            .Select(name => (name!.StartsWith("System"), name))
            .OrderByDescending(x => x.Item1)
            .ThenBy(x => x.name)
            .Select(x => new ModelUsing(x.name))
            .ToArray();

        return new MetadataModelDraft(new CsTypeDeclaration(type))
        {
            Attributes = attributes,
            OriginalInterfaces = interfaces
                .Where(x => x != modelInstanceInterfaceType)
                .Select(x => new CsTypeDeclaration(x))
                .ToArray(),
            ModelInstanceInterface = modelInstanceInterfaceType is not null
                ? new CsTypeDeclaration(modelInstanceInterfaceType)
                : null,
            Usings = usings,
            ValueProperties = valueProperties,
            RelationProperties = relationProperties,
            ImmutableType = new CsTypeDeclaration(generatedDeclaration.ImmutableType!),
            ImmutableFactory = generatedDeclaration.ImmutableFactory,
            MutableType = generatedDeclaration.MutableType is not null
                ? new CsTypeDeclaration(generatedDeclaration.MutableType)
                : null
        };
    }

    private static bool IsApplicationInterface(Type type)
    {
        return type.Namespace?.StartsWith("System") != true &&
            type.Namespace?.StartsWith("DataLinq.Instance") != true &&
            type.Namespace?.StartsWith("DataLinq.Interfaces") != true;
    }

    private static bool IsModelInstanceContract(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IModelInstance<>);

    private static MetadataRelationPropertyDraft ParseRelationPropertyDraft(
        PropertyInfo propertyInfo,
        CsTypeDeclaration csType,
        IReadOnlyList<Attribute> attributes) => new(
            propertyInfo.Name,
            csType)
        {
            Attributes = attributes,
            CsNullable = IsNullable(propertyInfo),
            RelationName = attributes
                .OfType<RelationAttribute>()
                .FirstOrDefault()?
                .Name
        };

    private static MetadataValuePropertyDraft ParseValuePropertyDraft(
        PropertyInfo propertyInfo,
        CsTypeDeclaration csType,
        IReadOnlyList<Attribute> attributes)
    {
        int? csSize;
        EnumProperty? enumProperty = null;

        if (csType.Type?.IsEnum == true)
        {
            csSize = MetadataTypeConverter.CsTypeSize("enum");

            var enumValueList = attributes.Any(attribute => attribute is EnumAttribute)
                ? attributes.OfType<EnumAttribute>().Single().Values.Select((x, i) => (x, i + 1)).ToList()
                : new List<(string name, int value)>();

            var enumType = csType.Type;
            var declaredInClass = enumType.DeclaringType == propertyInfo.DeclaringType;

            var enumValues = Enum.GetValuesAsUnderlyingType(enumType)
                .Cast<object>()
                .Select(Convert.ToInt32)
                .ToList();
            var csEnumValues = Enum.GetNames(enumType).Select((x, i) => (x, enumValues[i])).ToList();

            enumProperty = new EnumProperty(enumValueList, csEnumValues, declaredInClass);
        }
        else
        {
            csSize = MetadataTypeConverter.CsTypeSize(csType.Name);
        }

        return new MetadataValuePropertyDraft(
            propertyInfo.Name,
            csType,
            ParseColumnDraft(propertyInfo.Name, attributes))
        {
            Attributes = attributes,
            CsNullable = IsNullable(propertyInfo),
            CsSize = csSize,
            EnumProperty = enumProperty
        };
    }

    private static MetadataColumnDraft ParseColumnDraft(string propertyName, IEnumerable<Attribute> attributes)
    {
        var columnName = propertyName;
        foreach (var attribute in attributes)
        {
            if (attribute is ColumnAttribute columnAttribute)
                columnName = columnAttribute.Name;
        }

        return new MetadataColumnDraft(columnName)
        {
            Nullable = attributes.Any(x => x is NullableAttribute),
            AutoIncrement = attributes.Any(x => x is AutoIncrementAttribute),
            PrimaryKey = attributes.Any(x => x is PrimaryKeyAttribute),
            ForeignKey = attributes.Any(x => x is ForeignKeyAttribute),
            DbTypes = attributes
                .OfType<TypeAttribute>()
                .Select(x => new DatabaseColumnType(x.DatabaseType, x.Name, x.Length, x.Decimals, x.Signed))
                .ToArray()
        };
    }

    private static object? ParsePropertyDraft(this PropertyInfo propertyInfo)
    {
        var attributes = propertyInfo
                .GetCustomAttributes(false)
                .OfType<Attribute>()
                .ToArray();

        if (propertyInfo.Name == "EqualityContract" ||
            !attributes.Any(x => x is ColumnAttribute || x is RelationAttribute))
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
        var csType = new CsTypeDeclaration(type);

        return attributes.Any(attribute => attribute is RelationAttribute)
            ? ParseRelationPropertyDraft(propertyInfo, csType, attributes)
            : ParseValuePropertyDraft(propertyInfo, csType, attributes);
    }

    private static bool IsNullable(PropertyInfo propertyInfo)
    {
        if (Nullable.GetUnderlyingType(propertyInfo.PropertyType) != null)
            return true;

        if (propertyInfo.PropertyType.IsValueType)
            return false;

        return new NullabilityInfoContext()
            .Create(propertyInfo)
            .ReadState == NullabilityState.Nullable;
    }
}
