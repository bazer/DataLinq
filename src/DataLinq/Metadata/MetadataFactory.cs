using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Metadata;

public struct MetadataFromDatabaseFactoryOptions
{
    public bool CapitaliseNames { get; set; } = false;
    public bool DeclareEnumsInClass { get; set; } = false;
    public List<string> Tables { get; set; } = new List<string>();
    public List<string> Views { get; set; } = new List<string>();

    public MetadataFromDatabaseFactoryOptions()
    {
    }
}

public static class MetadataFactory
{
    public static void ParseIndices(DatabaseMetadata database)
    {
        var indices = database.TableModels
            .SelectMany(tableModel => tableModel.Table.Columns
                .Select(column => (column, indexAttributes: column.ValueProperty.Attributes.OfType<IndexAttribute>().ToList())))
            .Where(t => t.indexAttributes.Any());

        foreach (var (column, indexAttributes) in indices)
        {
            foreach (var indexAttribute in indexAttributes)
            {
                var existingIndex = column.Table.ColumnIndices.FirstOrDefault(x => x.Name == indexAttribute.Name);

                if (existingIndex != null)
                {
                    if (!existingIndex.Columns.Contains(column))
                        existingIndex.AddColumn(column);
                }
                else
                {
                    var columnsForIndex = indexAttribute.Columns.Any()
                        ? indexAttribute.Columns.Select(colName => column.Table.Columns.Single(c => c.DbName == colName)).ToList()
                        : new List<Column> { column };

                    column.Table.ColumnIndices.Add(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
                }
            }
        }
    }


    //public static void ParseIndices(DatabaseMetadata database)
    //{
    //    foreach (var column in database
    //        .TableModels.SelectMany(x => x.Table.Columns.Where(y => y.ValueProperty.Attributes.OfType<IndexAttribute>().Any())))
    //    {
    //        foreach (var indexAttribute in column.ValueProperty.Attributes.OfType<IndexAttribute>())
    //        {
    //            if (column.Table.ColumnIndices.Any(x => x.Name == indexAttribute.Name))
    //            {
    //                column.Table.ColumnIndices.Single(x => x.Name == indexAttribute.Name).Columns.Add(column);
    //            }
    //            else
    //            {
    //                column.Table.ColumnIndices.Add(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, new List<Column> { column }));
    //            }
    //        }
    //    }
    //}


    //public static void ParseIndices(DatabaseMetadata database)
    //{
    //    foreach (var column in database.
    //        TableModels.SelectMany(x => x.Table.Columns.Where(y => y.Unique)))
    //    {
    //        var uniqueAttribute = column.ValueProperty
    //            .Attributes
    //            .OfType<IndexAttribute>()
    //            .Single();

    //        if (column.Table.ColumnIndices.Any(x => x.Name == uniqueAttribute.Name))
    //        {
    //            column.Table.ColumnIndices.Single(x => x.Name == uniqueAttribute.Name).Columns.Add(column);
    //        }
    //        else
    //        {
    //            column.Table.ColumnIndices.Add(new ColumnIndex(uniqueAttribute.Name, IndexCharacteristic.Unique, IndexType.BTREE, new List<Column> { column }));
    //        }
    //    }
    //}

    public static void ParseRelations(DatabaseMetadata database)
    {
        foreach (var table in database.TableModels.Where(x => x.Table.Type == TableType.Table).Select(x => x.Table))
        {
            var columns = table.Columns.Where(x => x.PrimaryKey).ToList();

            if (!columns.Any())
                throw new Exception($"Table {table.DbName} is missing a primary key. Having a primary key for every table is a requirement for DataLinq.");

            table.ColumnIndices.Add(new ColumnIndex($"{table.DbName}_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, columns));
        }

        foreach (var column in database.TableModels.Where(x => x.Table.Type == TableType.Table).SelectMany(x => x.Table.Columns.Where(y => y.ForeignKey)))
        {
            foreach (var attribute in column.ValueProperty.Attributes.OfType<ForeignKeyAttribute>())
            {
                //var attribute = column.ValueProperty
                //    .Attributes
                //    .OfType<ForeignKeyAttribute>()
                //    .Single();

                var relation = new Relation
                {
                    ConstraintName = attribute.Name,
                    Type = RelationType.OneToMany
                };

                var candidateColumn = database
                    .TableModels.FirstOrDefault(x => x.Table.DbName == attribute.Table)
                    ?.Table.Columns.FirstOrDefault(x => x.DbName == attribute.Column);

                if (candidateColumn == null)
                    continue;

                if (!column.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.ForeignKey))
                    column.Table.ColumnIndices.Add(
                        new ColumnIndex(column.DbName, IndexCharacteristic.ForeignKey, IndexType.BTREE, [column]));

                if (!candidateColumn.ColumnIndices.Any())
                    candidateColumn.Table.ColumnIndices.Add(
                        new ColumnIndex(candidateColumn.DbName, IndexCharacteristic.VirtualDataLinq, IndexType.BTREE, [candidateColumn]));


                relation.ForeignKey = CreateRelationPart(relation, column.ColumnIndices.Last(), RelationPartType.ForeignKey);
                relation.CandidateKey = CreateRelationPart(relation, candidateColumn.ColumnIndices.First(), RelationPartType.CandidateKey);

                AttachRelationProperty(relation.ForeignKey, candidateColumn);
                AttachRelationProperty(relation.CandidateKey, column);
            }
        }
    }

    private static RelationPart CreateRelationPart(Relation relation, ColumnIndex column, RelationPartType type)
    {
        var relationPart = new RelationPart
        {
            Relation = relation,
            ColumnIndex = column,
            Type = type,
            CsName = column.Table.Model.CsTypeName
        };

        column.RelationParts.Add(relationPart);

        return relationPart;
    }

    private static RelationProperty AttachRelationProperty(RelationPart relationPart, Column column)
    {
        var property = relationPart.ColumnIndex.Table.Model
            .RelationProperties.SingleOrDefault(x =>
                x.Attributes.Any(y =>
                    y is RelationAttribute relationAttribute
                    && relationAttribute.Table == column.Table.DbName
                    && relationAttribute.Column == column.DbName));

        if (property == null)
        {
            property = new RelationProperty();
            property.Attributes.Add(new RelationAttribute(column.Table.DbName, column.DbName));
            property.CsName = column.Table.Database.TableModels.Single(x => x.Table == column.Table).CsPropertyName;
            property.Model = relationPart.ColumnIndex.Table.Model;
            relationPart.ColumnIndex.Table.Model.Properties.Add(property);
        }

        property.RelationPart = relationPart;

        return property;
    }

    public static ModelMetadata AttachModel(TableMetadata table, bool capitaliseNames)
    {
        var name = capitaliseNames
            ? table.DbName.FirstCharToUpper()
            : table.DbName;

        table.Model = new ModelMetadata
        {
            CsTypeName = name,
            Table = table,
            Database = table.Database
        };

        return table.Model;
    }

    public static ValueProperty AttachValueProperty(Column column, string csTypeName, bool capitaliseNames)
    {
        var name = capitaliseNames
            ? column.DbName.FirstCharToUpper()
            : column.DbName;

        var type = Type.GetType(csTypeName);

        var property = new ValueProperty
        {
            Column = column,
            Model = column.Table.Model,
            CsName = name,
            CsType = type,
            CsTypeName = csTypeName,
            CsSize = MetadataTypeConverter.CsTypeSize(csTypeName),
            CsNullable = column.Nullable && MetadataTypeConverter.IsCsTypeNullable(csTypeName)
        };

        property.Attributes = GetAttributes(property).ToList();
        column.ValueProperty = property;
        column.Table.Model.Properties.Add(column.ValueProperty);

        return property;
    }

    public static void AttachEnumProperty(ValueProperty property, IEnumerable<(string name, int value)> enumValues, IEnumerable<(string name, int value)> csEnumValues, bool declaredInClass)
    {
        property.EnumProperty = new EnumProperty(enumValues.ToList(), csEnumValues.ToList(), declaredInClass);
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
