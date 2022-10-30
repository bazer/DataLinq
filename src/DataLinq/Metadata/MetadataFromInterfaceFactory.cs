using DataLinq.Attributes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;

namespace DataLinq.Metadata
{
    public static class MetadataFromInterfaceFactory
    {
        public static DatabaseMetadata ParseDatabaseFromSources(params Type[] types)
        {
            var dbType =
                    types.FirstOrDefault(x => x.GetInterface("ICustomDatabaseModel") != null) ??
                    types.FirstOrDefault(x => x.GetInterface("IDatabaseModel") != null);

            var database = new DatabaseMetadata(dbType?.Name ?? "Unnamed", dbType);

            database.TableModels = types
                .Where(x =>
                    x.GetInterface("ICustomTableModel") != null ||
                    x.GetInterface("ICustomViewModel") != null)
                .Select(x => ParseTableModel(database, x, x.Name))
                .ToList();

            if (dbType != null)
            {
                ParseAttributes(database, dbType);

                var propertyModels = dbType
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(GetTableType)
                    .Select(x => database.ParseTableModel(x.type, x.csName))
                    .ToList();

                foreach (var propertyModel in propertyModels)
                {
                    var match = database.TableModels.FirstOrDefault(x => x.Table.DbName == propertyModel.Table.DbName);

                    if (match != null)
                        match.CsPropertyName = propertyModel.CsPropertyName;
                    else
                        database.TableModels.Add(propertyModel);
                }
            }

            MetadataFactory.ParseIndices(database);
            MetadataFactory.ParseRelations(database);

            return database;

        }

        public static DatabaseMetadata ParseDatabaseFromDatabaseModel(Type type)
        {
            var database = new DatabaseMetadata(type.Name, type);
            database.ParseAttributes(type);
            database.TableModels = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(GetTableType)
                .Select(x => database.ParseTableModel(x.type, x.csName))
                .ToList();

            MetadataFactory.ParseIndices(database);
            MetadataFactory.ParseRelations(database);

            return database;
        }

        private static TableModelMetadata ParseTableModel(this DatabaseMetadata database, Type type, string csPropertyName)
        {
            var model = database.ParseModel(type);

            return new TableModelMetadata
            {
                Model = model,
                Table = model.ParseTable(),
                CsPropertyName = csPropertyName
            };
        }

        private static void ParseAttributes(this DatabaseMetadata database, Type type)
        {
            foreach (var attribute in type.GetCustomAttributes(false))
            {
                if (attribute is DatabaseAttribute databaseAttribute)
                    database.Name = databaseAttribute.Name;

                if (attribute is UseCacheAttribute useCache)
                    database.UseCache = useCache.UseCache;

                if (attribute is CacheLimitAttribute cacheLimit)
                    database.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));
            }
        }

        private static (string csName, Type type) GetTableType(this PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type.GetGenericTypeDefinition() == typeof(DbRead<>))
                return (property.Name, type.GetGenericArguments()[0]);
            else
                throw new NotImplementedException();
        }

        private static ModelMetadata ParseModel(this DatabaseMetadata database, Type type)
        {
            var model = new ModelMetadata
            {
                Database = database,
                CsType = type,
                CsTypeName = type.Name,
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

        private static TableMetadata ParseTable(this ModelMetadata model)
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
                .Select(x => table.ParseColumn(x))
                .ToList();

            model.Table = table;

            return table;
        }

        private static Column ParseColumn(this TableMetadata table, ValueProperty property)
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
                    column.DbTypes.Add(new DatabaseColumnType
                    {
                        DatabaseType = t.DatabaseType,
                        Name = t.Name,
                        Length = t.Length,
                        Signed = t.Signed
                    });
                }
            }

            return column;
        }

        private static Property ParseProperty(this PropertyInfo propertyInfo, ModelMetadata model)
        {
            var attributes = propertyInfo
                    .GetCustomAttributes(false)
                    .OfType<Attribute>()
                    .ToList();

            var property = GetProperty(propertyInfo.PropertyType.IsEnum, attributes);

            property.Model = model;
            property.CsName = propertyInfo.Name;
            property.CsType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
            property.CsTypeName = MetadataTypeConverter.GetKeywordName(property.CsType);
            property.PropertyInfo = propertyInfo;
            property.Attributes = attributes;

            if (property is ValueProperty valueProp)
            {
                valueProp.CsNullable = propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>);

                if (property is EnumProperty enumProp)
                {
                    enumProp.CsSize = MetadataTypeConverter.CsTypeSize("enum");

                    if (attributes.Any(attribute => attribute is EnumAttribute))
                        enumProp.EnumValues = attributes.OfType<EnumAttribute>().Single().Values.ToList();
                    else
                        enumProp.EnumValues = Enum.GetNames(property.CsType).ToList();
                }
                else
                    valueProp.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
            }

            return property;
        }

        private static Property GetProperty(bool isEnum, List<Attribute> attributes)
        {
            if (isEnum)
                return new EnumProperty();

            if (attributes.Any(attribute => attribute is RelationAttribute))
                return new RelationProperty();

            if (attributes.Any(attribute => attribute is EnumAttribute))
                return new EnumProperty();

            return new ValueProperty();
        }
    }
}