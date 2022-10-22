﻿using DataLinq.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Metadata
{
    public static class MetadataFactory
    {
        public static void ParseIndices(DatabaseMetadata database)
        {
            foreach (var column in database.
                Tables.SelectMany(x => x.Columns.Where(y => y.Unique)))
            {
                var uniqueAttribute = column.ValueProperty
                    .Attributes
                    .OfType<UniqueAttribute>()
                    .Single();

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

        public static void ParseRelations(DatabaseMetadata database)
        {
            foreach (var column in database.
                Tables.SelectMany(x => x.Columns.Where(y => y.ForeignKey)))
            {
                var attribute = column.ValueProperty
                    .Attributes
                    .OfType<ForeignKeyAttribute>()
                    .Single();

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

        private static RelationProperty AttachRelationProperty(RelationPart relationPart, Column column)
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

            property.RelationPart = relationPart;

            return property;
        }

        public static ModelMetadata AttachModel(TableMetadata table)
        {
            table.Model = new ModelMetadata
            {
                CsTypeName = table.DbName,
                CsDatabasePropertyName = table.DbName,
                Table = table,
                Database = table.Database
            };

            return table.Model;
        }

        public static ValueProperty AttachValueProperty(Column column, string csTypeName)
        {
            var property = new ValueProperty
            {
                Column = column,
                Model = column.Table.Model,
                CsName = column.DbName,
                CsTypeName = csTypeName,
                CsSize = MetadataTypeConverter.CsTypeSize(csTypeName),
                CsNullable = column.Nullable && MetadataTypeConverter.IsCsTypeNullable(csTypeName)
            };

            property.Attributes = GetAttributes(property).ToList();
            column.ValueProperty = property;
            column.Table.Model.Properties.Add(column.ValueProperty);

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

            yield return new ColumnAttribute(column.DbName);

            foreach (var dbType in column.DbTypes)
            {
                yield return new TypeAttribute(dbType);

                //if (dbType.Length.HasValue && dbType.Signed.HasValue)
                //    yield return new TypeAttribute(dbType.DatabaseType, dbType.Name, column.Length.Value, column.Signed.Value);
                //else if (column.Length.HasValue)
                //    yield return new TypeAttribute(column.DbTypes, column.Length.Value);
                //else if (column.Signed.HasValue)
                //    yield return new TypeAttribute(column.DbTypes, column.Signed.Value);
                //else
                //    yield return new TypeAttribute(column.DbTypes);
            }
        }
    }
}
