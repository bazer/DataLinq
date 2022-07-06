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
                    ConstraintName = attribute.Name,
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
                if (attribute is NameAttribute nameAttribute)
                    table.DbName = nameAttribute.Name;

                if (attribute is UseCacheAttribute useCache)
                    table.UseCache = useCache.UseCache;

                if (attribute is CacheLimitAttribute cacheLimit)
                    table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

                if (table is ViewMetadata view && attribute is DefinitionAttribute definitionAttribute)
                    view.Definition = definitionAttribute.Sql;
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

                if (attribute is ForeignKeyAttribute)
                    column.ForeignKey = true;

                if (attribute is UniqueAttribute uniqueAttribute)
                {
                    if (column.Table.ColumnIndices.Any(x => x.ConstraintName == uniqueAttribute.Name))
                    {
                        column.Table.ColumnIndices.Single(x => x.ConstraintName == uniqueAttribute.Name).Columns.Add(column);
                    }
                    else
                    {
                        column.Table.ColumnIndices.Add(new ColumnIndex
                        {
                            Columns = new List<Column> { column },
                            ConstraintName = uniqueAttribute.Name,
                            Type = IndexType.Unique
                        });
                    }
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
                case "SByte":
                    return "sbyte";
                case "Byte":
                    return "byte";
                case "Int16":
                    return "short";
                case "UInt16":
                    return "ushort";
                case "Int32":
                    return "int";
                case "UInt32":
                    return "uint";
                case "Int64":
                    return "long";
                case "UInt64":
                    return "ulong";
                case "Char":
                    return "char";
                case "Single":
                    return "float";
                case "Double":
                    return "double";
                case "Boolean":
                    return "bool";
                case "Decimal":
                    return "decimal";
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
                "sbyte" => sizeof(sbyte),
                "byte" => sizeof(byte),
                "short" => sizeof(short),
                "ushort" => sizeof(ushort),
                "int" => sizeof(int),
                "uint" => sizeof(uint),
                "long" => sizeof(long),
                "ulong" => sizeof(ulong),
                "char" => sizeof(char),
                "float" => sizeof(float),
                "double" => sizeof(double),
                "bool" => sizeof(bool),
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