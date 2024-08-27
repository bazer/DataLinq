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

    private static TableModel ParseTableModel(this DatabaseDefinition database, Type type, string csPropertyName)
    {
        var model = database.ParseModel(type);

        return new TableModel
        {
            Model = model,
            Table = model.ParseTable(),
            CsPropertyName = csPropertyName
        };
    }

    private static (string csName, Type type) GetTableType(this PropertyInfo property)
    {
        var type = property.PropertyType;

        if (type.GetGenericTypeDefinition() == typeof(DbRead<>))
            return (property.Name, type.GetGenericArguments()[0]);
        else
            throw new NotImplementedException();
    }

    private static ModelDefinition ParseModel(this DatabaseDefinition database, Type type)
    {
        var model = new ModelDefinition
        {
            Database = database,
            CsType = new CsTypeDeclaration(type),
            Attributes = type.GetCustomAttributes(false).Cast<Attribute>().ToArray(),
            Interfaces = type.GetInterfaces().Select(x => new CsTypeDeclaration(x)).ToArray(),
            Usings = type.Namespace == null ? [] : [new ModelUsing { FullNamespaceName = type.Namespace }]
        };

        model.ImmutableType = FindType(type, $"{model.CsType.Namespace}.Immutable{model.CsType.Name}");
        model.MutableType = FindType(type, $"{model.CsType.Namespace}.Mutable{model.CsType.Name}");

        type
            .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(x => ParseProperty(x, model))
            .Where(x => x.Attributes.Any(x => x is ColumnAttribute || x is RelationAttribute))
            .Where(x => x.CsName != "EqualityContract")
            .ToList()
            .ForEach(model.AddProperty);

        model.Usings = model.ValueProperties.Values
            .Select(x => x.CsType?.Namespace)
            .Distinct()
            .Where(x => x != null)
            .Select(name => (name!.StartsWith("System"), name))
            .OrderByDescending(x => x.Item1)
            .ThenBy(x => x.name)
            .Select(x => new ModelUsing { FullNamespaceName = x.name })
            .ToArray();

        return model;
    }

    private static CsTypeDeclaration FindType(Type modelType, string name)
    {
        var type = modelType.Assembly.GetTypes().FirstOrDefault(x => x.FullName == name);

        return type == null
            ? throw new NotImplementedException($"Type '{name}' not found")
            : new CsTypeDeclaration(type);
    }

    private static TableDefinition ParseTable(this ModelDefinition model)
    {
        var table = model.CsType.Type?.GetInterfaces().Any(x => x.Name.StartsWith("ITableModel") || x.Name.StartsWith("ICustomTableModel")) == true
            ? new TableDefinition()
            : new ViewDefinition();

        table.Model = model;
        table.Database = model.Database;
        table.DbName = model.CsType.Name;

        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                table.DbName = tableAttribute.Name;

            if (attribute is UseCacheAttribute useCache)
                table.UseCache = useCache.UseCache;

            if (attribute is CacheLimitAttribute cacheLimit)
                table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                table.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (table is ViewDefinition view && attribute is DefinitionAttribute definitionAttribute)
                view.Definition = definitionAttribute.Sql;
        }

        table.Columns = model.ValueProperties.Values
            .Select(x => table.ParseColumn(x))
            .ToArray();

        model.Table = table;

        return table;
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