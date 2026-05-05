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
        ParseDatabaseFromGeneratedModel(type, GetGeneratedDatabaseModel(type)));

    private static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromGeneratedModel(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicProperties)]
        Type type,
        GeneratedDatabaseModelDeclaration generatedModel)
    {
        generatedModel.Validate(type);

        var database = new DatabaseDefinition(type.Name, csType: new CsTypeDeclaration(type));
        database.SetAttributes(type.GetCustomAttributes(false).Cast<Attribute>());
        database.ParseAttributes();
        database.SetTableModels(generatedModel.TableModels
            .Select(database.ParseTableModel));

        return new MetadataDefinitionFactory().Build(database);
    }

    private static TableModel ParseTableModel(this DatabaseDefinition database, GeneratedTableModelDeclaration declaration)
    {
        var model = declaration.ModelType.ParseModel(declaration);
        var table = ParseTable(model, declaration.TableType);
        return new TableModel(declaration.CsPropertyName, database, model, table);
    }

    private static TableDefinition ParseTable(ModelDefinition model, TableType tableType)
    {
        TableDefinition table = tableType switch
        {
            TableType.Table => new TableDefinition(model.CsType.Name),
            TableType.View => new ViewDefinition(model.CsType.Name),
            _ => throw new NotSupportedException($"Generated table type '{tableType}' is not supported for model '{model.CsType.Name}'.")
        };

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

    private static GeneratedDatabaseModelDeclaration GetGeneratedDatabaseModel(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type databaseType)
    {
        var generatedModelMethod = databaseType.GetMethod(
            GeneratedDatabaseModelMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (generatedModelMethod is not null)
        {
            if (generatedModelMethod.ReturnType != typeof(GeneratedDatabaseModelDeclaration))
            {
                throw new InvalidOperationException(
                    $"Generated metadata bootstrap method '{databaseType.FullName}.{GeneratedDatabaseModelMethodName}' must return '{typeof(GeneratedDatabaseModelDeclaration).FullName}'.");
            }

            return (GeneratedDatabaseModelDeclaration)generatedModelMethod.Invoke(null, null)!;
        }

        throw new InvalidOperationException(
            $"Database model '{databaseType.FullName}' is missing the generated DataLinq metadata hook '{GeneratedDatabaseModelMethodName}'. " +
            "Run the DataLinq source generator and ensure the database model is declared as a partial class.");
    }

    private static ModelDefinition ParseModel(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.NonPublicProperties |
            DynamicallyAccessedMemberTypes.Interfaces)]
        this Type type,
        GeneratedTableModelDeclaration generatedDeclaration)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(type));

        model.SetAttributes(type.GetCustomAttributes(false).Cast<Attribute>());

        var interfaces = type.GetInterfaces();
        var modelInstanceInterfaceType = interfaces.Any(IsModelInstanceContract)
            ? interfaces.FirstOrDefault(IsApplicationInterface)
            : null;

        model.SetInterfaces(interfaces
            .Where(x => x != modelInstanceInterfaceType)
            .Select(x => new CsTypeDeclaration(x)));
        model.SetModelInstanceInterface(
            modelInstanceInterfaceType is not null
                ? new CsTypeDeclaration(modelInstanceInterfaceType)
                : null);

        if (type.Namespace != null)
            model.SetUsings([new(type.Namespace)]);

        type
            .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(x => ParseProperty(x, model))
            .Where(x => x.Attributes.Any(x => x is ColumnAttribute || x is RelationAttribute))
            .Where(x => x.PropertyName != "EqualityContract")
            .ToList()
            .ForEach(model.AddProperty);

        model.SetUsings(model.ValueProperties.Values
            .Select(x => x.CsType.Namespace)
            .Distinct()
            .Where(x => x != null)
            .Select(name => (name!.StartsWith("System"), name))
            .OrderByDescending(x => x.Item1)
            .ThenBy(x => x.name)
            .Select(x => new ModelUsing(x.name)));

        if (generatedDeclaration.ImmutableType is null)
        {
            throw new InvalidOperationException(
                $"Generated table declaration for '{type.FullName}' is missing the immutable model type.");
        }

        if (generatedDeclaration.ImmutableFactory is null)
        {
            throw new InvalidOperationException(
                $"Generated table declaration for '{type.FullName}' is missing the immutable factory hook.");
        }

        model.SetImmutableType(new CsTypeDeclaration(generatedDeclaration.ImmutableType));
        model.SetImmutableFactory(generatedDeclaration.ImmutableFactory);

        if (generatedDeclaration.MutableType is not null)
            model.SetMutableType(new CsTypeDeclaration(generatedDeclaration.MutableType));

        return model;
    }

    private static bool IsApplicationInterface(Type type)
    {
        return type.Namespace?.StartsWith("System") != true &&
            type.Namespace?.StartsWith("DataLinq.Instance") != true &&
            type.Namespace?.StartsWith("DataLinq.Interfaces") != true;
    }

    private static bool IsModelInstanceContract(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IModelInstance<>);

    private static PropertyDefinition ParseProperty(this PropertyInfo propertyInfo, ModelDefinition model)
    {
        var attributes = propertyInfo
                .GetCustomAttributes(false)
                .OfType<Attribute>()
                .ToList();

        var type = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
        PropertyDefinition property = attributes.Any(attribute => attribute is RelationAttribute)
            ? new RelationProperty(propertyInfo.Name, new CsTypeDeclaration(type), model, attributes)
            : new ValueProperty(propertyInfo.Name, new CsTypeDeclaration(type), model, attributes);

        property.SetCsNullable(IsNullable(propertyInfo));

        if (property is ValueProperty valueProp)
        {
            if (property.CsType.Type?.IsEnum == true)
            {
                valueProp.SetCsSize(MetadataTypeConverter.CsTypeSize("enum"));

                var enumValueList = attributes.Any(attribute => attribute is EnumAttribute)
                    ? attributes.OfType<EnumAttribute>().Single().Values.Select((x, i) => (x, i + 1)).ToList()
                    : new List<(string name, int value)>();

                var enumType = property.CsType.Type;
                var declaredInClass = enumType.DeclaringType == model.CsType.Type;

                var enumValues = Enum.GetValuesAsUnderlyingType(enumType)
                    .Cast<object>()
                    .Select(Convert.ToInt32)
                    .ToList();
                var csEnumValues = Enum.GetNames(enumType).Select((x, i) => (x, enumValues[i])).ToList();

                valueProp.SetEnumProperty(new EnumProperty(enumValueList, csEnumValues, declaredInClass));
            }
            else
                valueProp.SetCsSize(MetadataTypeConverter.CsTypeSize(property.CsType.Name));
        }

        return property;
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
