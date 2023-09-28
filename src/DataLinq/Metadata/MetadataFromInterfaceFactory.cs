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
        public static DatabaseMetadata ParseDatabaseFromSources(bool removeInterfacePrefix, params Type[] types)
        {
            var dbType =
                    types.FirstOrDefault(x => x.GetInterface("ICustomDatabaseModel") != null) ??
                    types.FirstOrDefault(x => x.GetInterface("IDatabaseModel") != null);

            var database = new DatabaseMetadata(dbType?.Name ?? "Unnamed", dbType);

            var customModels = types
                .Where(x =>
                    x.GetInterface("ICustomTableModel") != null ||
                    x.GetInterface("ICustomViewModel") != null)
                .Select(x => ParseTableModel(database, x, x.Name))
                .ToList();

            if (dbType != null)
            {
                ParseAttributes(database, dbType);

                database.TableModels = dbType
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(GetTableType)
                    .Select(x => database.ParseTableModel(x.type, x.csName))
                    .ToList();

                var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix));

                foreach (var customModel in customModels)
                {
                    var match = database.TableModels.FirstOrDefault(x => x.Table.DbName == customModel.Table.DbName);

                    if (match != null)
                    {
                        transformer.TransformTable(customModel, match);
                        //match.CsPropertyName = customModel.CsPropertyName;
                    }
                    else
                        database.TableModels.Add(customModel);
                }
            }
            else
            {
                database.TableModels = customModels;
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

                if (attribute is CacheCleanupAttribute cacheCleanup)
                    database.CacheCleanup.Add((cacheCleanup.LimitType, cacheCleanup.Amount));
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
            var table = model.CsType.GetInterfaces().Any(x => x.Name.StartsWith("ITableModel") || x.Name.StartsWith("ICustomTableModel"))
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
                    column.Unique = true;

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

                    //if (attributes.Any(attribute => attribute is EnumAttribute))
                    //    valueProp.EnumProperty.Value.EnumValues = attributes.OfType<EnumAttribute>().Single().Values.ToList();
                    //else
                    //    enumProp.EnumValues = Enum.GetNames(property.CsType).ToList();
                }
                else
                    valueProp.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
            }

            return property;
        }

        private static Property GetProperty(List<Attribute> attributes)
        {
            //if (isEnum)
            //    return new EnumProperty();

            if (attributes.Any(attribute => attribute is RelationAttribute))
                return new RelationProperty();

            //if (attributes.Any(attribute => attribute is EnumAttribute))
            //    return new EnumProperty();

            return new ValueProperty();
        }
    }
}