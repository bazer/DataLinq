using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DataLinq.Attributes;
using DataLinq.Cache;

namespace DataLinq.Metadata
{
    public static class MetadataFromInterfaceFactory
    {
        public static DatabaseMetadata ParseDatabase(Type type)
        {
            var database = new DatabaseMetadata(type.Name);

            foreach (var attribute in type.GetCustomAttributes(false))
            {
                if (attribute is NameAttribute nameAttribute)
                    database.Name = nameAttribute.Name;

                if (attribute is UseCacheAttribute useCache)
                    database.UseCache = useCache.UseCache;

                if (attribute is CacheLimitAttribute cacheLimit)
                    database.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));
            }

            database.Models = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(GetTableType)
                .Select(x => ParseModel(database, x))
                .ToList();

            database.Tables = database
                .Models.Select(ParseTable)
                .ToList();

            database.Relations = ParseRelations(database)
                .ToList();

            return database;
        }

        private static IEnumerable<Relation> ParseRelations(DatabaseMetadata database)
        {
            foreach (var column in database.
                Tables.SelectMany(x => x.Columns.Where(y => y.ForeignKey)))
            {
                var attribute = column.ValueProperty
                    .Attributes.Single(x => x is ForeignKeyAttribute) as ForeignKeyAttribute;

                var relation = new Relation
                {
                    Constraint = attribute.Name,
                    Type = RelationType.OneToMany
                };

                var candidateColumn = database
                    .Tables.Single(x => x.DbName == attribute.Table)
                    .Columns.Single(x => x.DbName == attribute.Column);

                relation.ForeignKey = CreateRelationPart(relation, column, RelationPartType.ForeignKey);
                relation.CandidateKey = CreateRelationPart(relation, candidateColumn, RelationPartType.CandidateKey);

                AttachRelationProperty(relation.ForeignKey, candidateColumn);
                AttachRelationProperty(relation.CandidateKey, column);

                yield return relation;
            }
        }

        private static RelationPart CreateRelationPart(Relation relation, Column column, RelationPartType type)
        {
            var relationPart = new RelationPart
            {
                Relation = relation,
                Column = column,
                Type = type
            };

            column.RelationParts.Add(relationPart);

            return relationPart;
        }

        private static Property AttachRelationProperty(RelationPart relationPart, Column column)
        {
            var property = relationPart.Column.Table.Model
                .Properties.SingleOrDefault(x =>
                    x.Attributes.Any(y =>
                        y is RelationAttribute relationAttribute
                        && relationAttribute.Table == column.Table.DbName
                        && relationAttribute.Column == column.DbName));

            property.Column = column;
            property.Type = PropertyType.Relation;
            property.RelationPart = relationPart;
            column.RelationProperties.Add(property);

            return property;
        }

        private static Type GetTableType(PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type.GetGenericTypeDefinition() == typeof(DbRead<>))
                return type.GetGenericArguments()[0];
            else
                throw new NotImplementedException();
        }

        private static ModelMetadata ParseModel(DatabaseMetadata database, Type type)
        {
            var model = new ModelMetadata
            {
                Database = database,
                CsType = type,
                CsTypeName = type.Name,
                Attributes = type.GetCustomAttributes(false)
            };

            model.Properties = type
                .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x => ParseProperty(x, model))
                .ToList();

            return model;
        }

        private static TableMetadata ParseTable(ModelMetadata model)
        {
            var table = new TableMetadata
            {
                Model = model,
                Database = model.Database,
                DbName = model.CsTypeName,
                Type = model.CsType.GetInterfaces().Any(x => x.Name == "ITableModel")
                    ? TableType.Table
                    : TableType.View
            };

            foreach (var attribute in model.Attributes)
            {
                if (attribute is NameAttribute nameAttribute)
                    table.DbName = nameAttribute.Name;

                if (attribute is UseCacheAttribute useCache)
                    table.UseCache = useCache.UseCache;

                if (attribute is CacheLimitAttribute cacheLimit)
                    table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));
            }

            table.Columns = model.Properties
                .Where(x => !x.Attributes.Any(attribute => attribute is RelationAttribute))
                .Select(x => ParseColumn(table, x))
                .ToList();

            model.Table = table;
            //table.Cache = new TableCache(table);

            return table;
        }

        private static Column ParseColumn(TableMetadata table, Property property)
        {
            var column = new Column
            {
                Table = table,
                DbName = property.PropertyInfo.Name,
                ValueProperty = property
            };

            property.Column = column;
            property.Type = PropertyType.Value;

            foreach (var attribute in property.Attributes)
            {
                if (attribute is NameAttribute nameAttribute)
                    column.DbName = nameAttribute.Name;

                if (attribute is NullableAttribute)
                    column.Nullable = true;

                if (attribute is AutoIncrementAttribute)
                    column.AutoIncrement = true;

                if (attribute is PrimaryKeyAttribute)
                    column.PrimaryKey = true;

                if (attribute is ForeignKeyAttribute foreignKeyAttribute)
                    column.ForeignKey = true;

                if (attribute is TypeAttribute t)
                {
                    column.DbType = t.Name;
                    column.Length = t.Length;
                }
            }

            return column;
        }

        private static Property ParseProperty(PropertyInfo propertyInfo, ModelMetadata model)
        {
            var property = new Property
            {
                Model = model,
                CsName = propertyInfo.Name,
                CsType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType,
                CsNullable = propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>),
                PropertyInfo = propertyInfo,
                Attributes = propertyInfo.GetCustomAttributes(false),
            };

            property.CsTypeName = GetKeywordName(property.CsType);
            property.CsSize = CsTypeSize(property.CsTypeName);

            return property;
        }

        private static string GetKeywordName(Type type)
        {
            switch (type.Name)
            {
                case "Int32":
                    return "int";
                case "Int64":
                    return "long";
                default:
                    return type.Name;
            }
        }

        private static int? CsTypeSize(string csType)
        {
            //if (csType.StartsWith("IEnumerable"))
            //    return null;

            return csType switch
            {
                "bool" => sizeof(bool),
                "int" => sizeof(int),
                "long" => sizeof(long),
                "float" => sizeof(float),
                "double" => sizeof(double),
                "decimal" => sizeof(decimal),
                "DateTime" => 8,
                "DateOnly" => sizeof(int),
                "Guid" => 16,
                "String" => null,
                "byte[]" => null,
                _ => null
            };
        }
    }
}