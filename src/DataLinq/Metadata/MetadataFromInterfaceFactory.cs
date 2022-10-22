using DataLinq.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DataLinq.Metadata
{
    public static class MetadataFromInterfaceFactory
    {
        public static DatabaseMetadata ParseDatabase(Type type)
        {
            var database = new DatabaseMetadata(type.Name);

            foreach (var attribute in type.GetCustomAttributes(false))
            {
                if (attribute is DatabaseAttribute databaseAttribute)
                    database.Name = databaseAttribute.Name;

                if (attribute is UseCacheAttribute useCache)
                    database.UseCache = useCache.UseCache;

                if (attribute is CacheLimitAttribute cacheLimit)
                    database.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));
            }

            database.Models = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(GetTableType)
                .Select(x => ParseModel(database, x.type, x.csName))
                .ToList();

            database.Tables = database
                .Models.Select(ParseTable)
                .ToList();

            MetadataFactory.ParseIndices(database);
            MetadataFactory.ParseRelations(database);

            return database;
        }

        private static (string csName, Type type) GetTableType(PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type.GetGenericTypeDefinition() == typeof(DbRead<>))
                return (property.Name, type.GetGenericArguments()[0]);
            else
                throw new NotImplementedException();
        }

        private static ModelMetadata ParseModel(DatabaseMetadata database, Type type, string csPropertyName)
        {
            var model = new ModelMetadata
            {
                Database = database,
                CsType = type,
                CsTypeName = type.Name,
                CsDatabasePropertyName = csPropertyName,
                Attributes = type.GetCustomAttributes(false),
                Interfaces = type.GetInterfaces()
            };

            model.Properties = type
                .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x => ParseProperty(x, model))
                .Where(x => x.CsName != "EqualityContract")
                .ToList();

            return model;
        }

        private static TableMetadata ParseTable(ModelMetadata model)
        {
            var table = model.CsType.GetInterfaces().Any(x => x.Name == "ITableModel")
                ? new TableMetadata()
                : new ViewMetadata();

            table.Model = model;
            table.Database = model.Database;
            table.DbName = model.CsTypeName;

            foreach (var attribute in model.Attributes)
            {
                if (attribute is TableAttribute tableAttribute)
                    table.DbName = tableAttribute.Name;

                if (attribute is UseCacheAttribute useCache)
                    table.UseCache = useCache.UseCache;

                if (attribute is CacheLimitAttribute cacheLimit)
                    table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

                if (table is ViewMetadata view && attribute is DefinitionAttribute definitionAttribute)
                    view.Definition = definitionAttribute.Sql;
            }

            table.Columns = model.ValueProperties
                .Select(x => ParseColumn(table, x))
                .ToList();

            model.Table = table;

            return table;
        }

        private static Column ParseColumn(TableMetadata table, ValueProperty property)
        {
            var column = new Column
            {
                Table = table,
                DbName = property.PropertyInfo.Name,
                ValueProperty = property
            };

            property.Column = column;

            foreach (var attribute in property.Attributes)
            {
                if (attribute is ColumnAttribute columnAttribute)
                    column.DbName = columnAttribute.Name;

                if (attribute is NullableAttribute)
                    column.Nullable = true;

                if (attribute is AutoIncrementAttribute)
                    column.AutoIncrement = true;

                if (attribute is PrimaryKeyAttribute)
                    column.PrimaryKey = true;

                if (attribute is ForeignKeyAttribute)
                    column.ForeignKey = true;

                if (attribute is UniqueAttribute)
                {
                    column.Unique = true;
                }

                if (attribute is TypeAttribute t)
                {
                    column.DbType = t.Name;
                    column.Length = t.Length;
                    column.Signed = t.Signed;
                }
            }

            return column;
        }

        private static Property ParseProperty(PropertyInfo propertyInfo, ModelMetadata model)
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
                valueProp.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
            }

            return property;

            Property GetProperty(List<Attribute> attributes) => attributes.Any(attribute => attribute is RelationAttribute)
                ? new RelationProperty()
                : new ValueProperty();
        }
    }
}