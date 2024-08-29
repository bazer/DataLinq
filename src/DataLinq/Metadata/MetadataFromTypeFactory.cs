using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using DataLinq.Attributes;
using DataLinq.Core.Factories;

namespace DataLinq.Metadata;

public static class MetadataFromTypeFactory
{
    public static DatabaseDefinition ParseDatabaseFromDatabaseModel(Type type)
    {
        var database = new DatabaseDefinition(type.Name, csType: new CsTypeDeclaration(type));
        database.SetAttributes(type.GetCustomAttributes(false).Cast<Attribute>());
        database.ParseAttributes();
        database.SetTableModels(type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(GetTableType)
            .Select(x => database.ParseTableModel(x.type, x.csName)));

        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);

        return database;
    }

    private static TableModel ParseTableModel(this DatabaseDefinition database, Type type, string csPropertyName) =>
        new(csPropertyName, database, type.ParseModel());

    private static (string csName, Type type) GetTableType(this PropertyInfo property)
    {
        var type = property.PropertyType;

        if (type.GetGenericTypeDefinition() == typeof(DbRead<>))
            return (property.Name, type.GetGenericArguments()[0]);
        else
            throw new NotImplementedException();
    }

    private static ModelDefinition ParseModel(this Type type)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(type));

        model.SetAttributes(type.GetCustomAttributes(false).Cast<Attribute>());
        model.SetInterfaces(type.GetInterfaces().Select(x => new CsTypeDeclaration(x)));

        if (type.Namespace != null)
            model.SetUsings([new(type.Namespace)]);

        model.SetImmutableType(FindType(type, $"{model.CsType.Namespace}.Immutable{model.CsType.Name}"));
        model.SetMutableType(FindType(type, $"{model.CsType.Namespace}.Mutable{model.CsType.Name}"));

        type
            .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(x => ParseProperty(x, model))
            .Where(x => x.Attributes.Any(x => x is ColumnAttribute || x is RelationAttribute))
            .Where(x => x.CsName != "EqualityContract")
            .ToList()
            .ForEach(model.AddProperty);

        model.SetUsings(model.ValueProperties.Values
            .Select(x => x.CsType?.Namespace)
            .Distinct()
            .Where(x => x != null)
            .Select(name => (name!.StartsWith("System"), name))
            .OrderByDescending(x => x.Item1)
            .ThenBy(x => x.name)
            .Select(x => new ModelUsing(x.name)));

        return model;
    }

    private static CsTypeDeclaration FindType(Type modelType, string name)
    {
        var type = modelType.Assembly.GetTypes().FirstOrDefault(x => x.FullName == name);

        return type == null
            ? throw new NotImplementedException($"Type '{name}' not found")
            : new CsTypeDeclaration(type);
    }

   

    private static PropertyDefinition ParseProperty(this PropertyInfo propertyInfo, ModelDefinition model)
    {
        var attributes = propertyInfo
                .GetCustomAttributes(false)
                .OfType<Attribute>()
                .ToList();

        var property = GetProperty(attributes);

        property.Model = model;
        property.CsName = propertyInfo.Name;
        property.CsType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
        property.CsTypeName = MetadataTypeConverter.GetKeywordName(property.CsType);
        property.PropertyInfo = propertyInfo;
        property.Attributes = attributes;

        if (property is ValueProperty valueProp)
        {
            valueProp.CsNullable = propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>);

            if (property.CsType.IsEnum)
            {
                valueProp.CsSize = MetadataTypeConverter.CsTypeSize("enum");

                var enumValueList = attributes.Any(attribute => attribute is EnumAttribute)
                    ? attributes.OfType<EnumAttribute>().Single().Values.Select((x, i) => (x, i)).ToList()
                    : new List<(string name, int value)>();

                var enumValues = Enum.GetValues(property.CsType).Cast<int>().ToList();
                valueProp.EnumProperty = new EnumProperty(enumValueList, Enum.GetNames(property.CsType).Select((x, i) => (x, enumValues[i])).ToList(), true);
            }
            else
                valueProp.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
        }

        return property;
    }

    private static PropertyDefinition GetProperty(List<Attribute> attributes)
    {
        if (attributes.Any(attribute => attribute is RelationAttribute))
            return new RelationProperty();

        return new ValueProperty();
    }
}