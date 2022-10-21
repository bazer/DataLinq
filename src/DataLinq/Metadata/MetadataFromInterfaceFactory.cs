using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
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

            ParseIndices(database);

            database.Relations = ParseRelations(database)
                .ToList();

            return database;
        }

        public static void ParseIndices(DatabaseMetadata database)
        {
            foreach (var column in database.
                Tables.SelectMany(x => x.Columns.Where(y => y.Unique)))
            {
                var uniqueAttribute = column.ValueProperty
                    .Attributes.Single(x => x is UniqueAttribute) as UniqueAttribute;

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
        }

        public static IEnumerable<Relation> ParseRelations(DatabaseMetadata database)
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
                    .Tables.FirstOrDefault(x => x.DbName == attribute.Table)?
                    .Columns.FirstOrDefault(x => x.DbName == attribute.Column);

                if (candidateColumn == null)
                    continue;

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
                Type = type,
                CsName = column.Table.Model.CsTypeName
            };

            column.RelationParts.Add(relationPart);

            return relationPart;
        }

        public static RelationProperty AttachRelationProperty(RelationPart relationPart, Column column)
        {
            var property = relationPart.Column.Table.Model
                .RelationProperties.SingleOrDefault(x => 
                    x.Attributes.Any(y =>
                        y is RelationAttribute relationAttribute
                        && relationAttribute.Table == column.Table.DbName
                        && relationAttribute.Column == column.DbName));

            if (property == null)
            {
                property = new RelationProperty();
                property.Attributes.Add(new RelationAttribute(column.Table.DbName, column.DbName));
                property.CsName = column.Table.Model.CsDatabasePropertyName;
                property.Model = relationPart.Column.Table.Model;
                relationPart.Column.Table.Model.Properties.Add(property);
            }
            //property.Column = column;
            //property.Type = PropertyType.Relation;
            property.RelationPart = relationPart;
            column.RelationProperties.Add(property);

            return property;
        }

        

        public static IEnumerable<Attribute> GetAttributes(ValueProperty property)
        {
            var column = property.Column;

            if (column.PrimaryKey)
                yield return new PrimaryKeyAttribute();

            if (column.AutoIncrement)
                yield return new AutoIncrementAttribute();

            if (column.Nullable)
                yield return new NullableAttribute();

            //if (property.Type == PropertyType.Value)
            //{
                yield return new ColumnAttribute(column.DbName);

                if (column.Length.HasValue && column.Signed.HasValue)
                    yield return new TypeAttribute(column.DbType, column.Length.Value, column.Signed.Value);
                else if (column.Length.HasValue)
                    yield return new TypeAttribute(column.DbType, column.Length.Value);
                else if (column.Signed.HasValue)
                    yield return new TypeAttribute(column.DbType, column.Signed.Value);
                else
                    yield return new TypeAttribute(column.DbType);
//            }

            //foreach (var index in column.Table.ColumnIndices.Where(x => x.Columns.Contains(column)))
            //{
            //    if (index.Type == IndexType.Unique)
            //        yield return new UniqueAttribute(index.ConstraintName);
            //}

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
                //.Where(x => !x.Attributes.Any(attribute => attribute is RelationAttribute))
                //.Where(x => x.Type == PropertyType.Value)
                .Select(x => ParseColumn(table, x))
                .ToList();

            model.Table = table;
            //table.Cache = new TableCache(table);

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
            //property.Type = PropertyType.Value;

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
                    //if (column.Table.ColumnIndices.Any(x => x.ConstraintName == uniqueAttribute.Name))
                    //{
                    //    column.Table.ColumnIndices.Single(x => x.ConstraintName == uniqueAttribute.Name).Columns.Add(column);
                    //}
                    //else
                    //{
                    //    column.Table.ColumnIndices.Add(new ColumnIndex
                    //    {
                    //        Columns = new List<Column> { column },
                    //        ConstraintName = uniqueAttribute.Name,
                    //        Type = IndexType.Unique
                    //    });
                    //}
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

        public static ValueProperty ParseProperty(Column column)
        {
            var property = new ValueProperty();
            property.Column = column;
            property.Model = column.Table.Model;
            property.CsName = column.DbName;
            property.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
            property.CsTypeName = MetadataTypeConverter.ParseCsType(column.DbType);
            property.CsNullable = column.Nullable && MetadataTypeConverter.IsCsTypeNullable(property.CsTypeName);
            property.Attributes = GetAttributes(property).ToList();

            return property;
        }

        private static Property ParseProperty(PropertyInfo propertyInfo, ModelMetadata model)
        {
            var attributes = propertyInfo
                    .GetCustomAttributes(false)
                    .Select(x => x as Attribute)
                    .Where(x => x != null)
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

            //var property = new Property
            //{
            //    Model = model,
            //    CsName = propertyInfo.Name,
            //    CsType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType,
            //    CsNullable = propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>),
            //    PropertyInfo = propertyInfo,
            //    Attributes = propertyInfo
            //        .GetCustomAttributes(false)
            //        .Select(x => x as Attribute)
            //        .Where(x => x != null)
            //        .ToList(),
            //};


            return property;


            Property GetProperty(List<Attribute> attributes) => attributes.Any(attribute => attribute is RelationAttribute)
                ? new RelationProperty()
                : new ValueProperty();
        }




    }
}