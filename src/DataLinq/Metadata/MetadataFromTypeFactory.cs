using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Interfaces;
using ThrowAway;

namespace DataLinq.Metadata;

public static class MetadataFromTypeFactory
{
    private const string GeneratedDatabaseModelMethodName = "GetDataLinqGeneratedModel";
    private const string GeneratedTableModelsMethodName = "GetDataLinqGeneratedTableModels";

    public static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromDatabaseModel<TDatabase>()
        where TDatabase : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<TDatabase> =>
        DLOptionFailure.CatchAll(() => ParseDatabaseFromGeneratedModel(typeof(TDatabase), TDatabase.GetDataLinqGeneratedModel()));

    public static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromDatabaseModel(Type type) => DLOptionFailure.CatchAll(() =>
        ParseDatabaseFromGeneratedModel(type, GetGeneratedDatabaseModel(type)));

    private static DatabaseDefinition ParseDatabaseFromGeneratedModel(Type type, GeneratedDatabaseModelDeclaration generatedModel)
    {
        var database = new DatabaseDefinition(type.Name, csType: new CsTypeDeclaration(type));
        database.SetAttributes(type.GetCustomAttributes(false).Cast<Attribute>());
        database.ParseAttributes();
        database.SetTableModels(generatedModel.TableModels
            .Select(database.ParseTableModel));

        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);
        MetadataFactory.IndexColumns(database);

        return database;
    }

    private static TableModel ParseTableModel(this DatabaseDefinition database, GeneratedTableModelDeclaration declaration) =>
        new(declaration.CsPropertyName, database, declaration.ModelType.ParseModel(declaration));

    private static GeneratedDatabaseModelDeclaration GetGeneratedDatabaseModel(Type databaseType)
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

        var generatedTableModelsMethod = databaseType.GetMethod(
            GeneratedTableModelsMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (generatedTableModelsMethod is null)
        {
            throw new InvalidOperationException(
                $"Database model '{databaseType.FullName}' is missing the generated DataLinq metadata hook. " +
                "Run the DataLinq source generator and ensure the database model is declared as a partial class.");
        }

        if (!typeof(IEnumerable<GeneratedTableModelDeclaration>).IsAssignableFrom(generatedTableModelsMethod.ReturnType))
        {
            throw new InvalidOperationException(
                $"Generated metadata bootstrap method '{databaseType.FullName}.{GeneratedTableModelsMethodName}' must return '{typeof(IEnumerable<GeneratedTableModelDeclaration>).FullName}'.");
        }

        var declarations = (IEnumerable<GeneratedTableModelDeclaration>?)generatedTableModelsMethod.Invoke(null, null);
        if (declarations is null)
        {
            throw new InvalidOperationException(
                $"Generated metadata bootstrap method '{databaseType.FullName}.{GeneratedTableModelsMethodName}' returned null.");
        }

        return new GeneratedDatabaseModelDeclaration(declarations.ToArray());
    }

    private static ModelDefinition ParseModel(this Type type, GeneratedTableModelDeclaration generatedDeclaration)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(type));

        model.SetAttributes(type.GetCustomAttributes(false).Cast<Attribute>());

        model.SetInterfaces(type.GetInterfaces().Where(x => !IsModelInstanceInterface(x)).Select(x => new CsTypeDeclaration(x)));
        model.SetModelInstanceInterface(type.GetInterfaces().Where(IsModelInstanceInterface).Select(x => new CsTypeDeclaration(x)).FirstOrDefault());

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

    private static bool IsModelInstanceInterface(Type type)
    {
        if (type.Namespace?.StartsWith("System") == true ||
            type.Namespace?.StartsWith("DataLinq.Instance") == true ||
            type.Namespace?.StartsWith("DataLinq.Interfaces") == true)
            return false;

        return type.GetInterfaces().Any(x => x.Name.StartsWith("IModelInstance"));
    }

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

        if (property is ValueProperty valueProp)
        {
            valueProp.SetCsNullable(propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>));

            if (property.CsType.Type?.IsEnum == true)
            {
                valueProp.SetCsSize(MetadataTypeConverter.CsTypeSize("enum"));

                var enumValueList = attributes.Any(attribute => attribute is EnumAttribute)
                    ? attributes.OfType<EnumAttribute>().Single().Values.Select((x, i) => (x, i + 1)).ToList()
                    : new List<(string name, int value)>();

                var enumType = property.CsType.Type;
                var declaredInClass = enumType.DeclaringType == model.CsType.Type;

                var enumValues = Enum.GetValues(enumType).Cast<int>().ToList();
                var csEnumValues = Enum.GetNames(enumType).Select((x, i) => (x, enumValues[i])).ToList();

                valueProp.SetEnumProperty(new EnumProperty(enumValueList, csEnumValues, declaredInClass));
            }
            else
                valueProp.SetCsSize(MetadataTypeConverter.CsTypeSize(property.CsType.Name));
        }

        return property;
    }
}
