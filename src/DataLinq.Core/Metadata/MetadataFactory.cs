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

                var foreignKeyIndex = column.ColumnIndices.Last();
                var candidateKeyIndex = candidateColumn.ColumnIndices.First();

                relation.ForeignKey = CreateRelationPart(relation, foreignKeyIndex, RelationPartType.ForeignKey);
                relation.CandidateKey = CreateRelationPart(relation, candidateKeyIndex, RelationPartType.CandidateKey);

                var candidateProperty = GetRelationProperty(relation.ForeignKey, candidateColumn);
                var columnProperty = GetRelationProperty(relation.CandidateKey, column);

                if (candidateProperty != null && columnProperty != null)
                {
                    foreignKeyIndex.RelationParts.Add(relation.ForeignKey);
                    candidateKeyIndex.RelationParts.Add(relation.CandidateKey);

                    candidateProperty.RelationPart = relation.ForeignKey;
                    columnProperty.RelationPart = relation.CandidateKey;
                }
            }
        }
    }

    private static RelationPart CreateRelationPart(Relation relation, ColumnIndex column, RelationPartType type)
    {
        return new RelationPart
        {
            Relation = relation,
            ColumnIndex = column,
            Type = type,
            CsName = column.Table.Model.CsTypeName
        };
    }

    private static RelationProperty? GetRelationProperty(RelationPart relationPart, Column column)
    {
        return relationPart.ColumnIndex.Table.Model
            .RelationProperties.Values.SingleOrDefault(x =>
                x.Attributes.Any(y =>
                    y is RelationAttribute relationAttribute
                    && relationAttribute.Table == column.Table.DbName
                    && relationAttribute.Columns[0] == column.DbName
                    && (relationAttribute.Name == null || relationAttribute.Name == relationPart.Relation.ConstraintName)));
    }

    public static RelationProperty AddRelationProperty(Column column, Column referencedColumn, string constraintName)
    {
        var relationProperty = new RelationProperty();
        relationProperty.Attributes.Add(new RelationAttribute(referencedColumn.Table.DbName, referencedColumn.DbName, constraintName));
        relationProperty.Model = column.Table.Model;
        relationProperty.CsName = referencedColumn.Table.DbName;
        relationProperty.RelationName = constraintName;

        var i = 2;
        while (relationProperty.Model.RelationProperties.ContainsKey(relationProperty.CsName))
            relationProperty.CsName = relationProperty.CsName + "_" + i++;

        relationProperty.Model.AddProperty(relationProperty);

        return relationProperty;
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
        column.Table.Model.AddProperty(column.ValueProperty);

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

    public static void ParseAttributes(this DatabaseMetadata database)
    {
        foreach (var attribute in database.Attributes)
        {
            if (attribute is DatabaseAttribute databaseAttribute)
                database.Name = databaseAttribute.Name;

            if (attribute is UseCacheAttribute useCache)
                database.UseCache = useCache.UseCache;

            if (attribute is CacheLimitAttribute cacheLimit)
                database.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                database.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (attribute is CacheCleanupAttribute cacheCleanup)
                database.CacheCleanup.Add((cacheCleanup.LimitType, cacheCleanup.Amount));
        }
    }

    public static Column ParseColumn(this TableMetadata table, ValueProperty property)
    {
        var column = new Column
        {
            Table = table,
            DbName = property.PropertyInfo?.Name,
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
                column.SetPrimaryKey(true);

            if (attribute is ForeignKeyAttribute)
                column.ForeignKey = true;

            if (attribute is TypeAttribute t)
            {
                column.AddDbType(new DatabaseColumnType
                {
                    DatabaseType = t.DatabaseType,
                    Name = t.Name,
                    Length = t.Length,
                    Decimals = t.Decimals,
                    Signed = t.Signed
                });
            }
        }

        return column;
    }
}
