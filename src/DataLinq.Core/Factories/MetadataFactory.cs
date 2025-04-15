using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.Core.Factories;

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
    public static void ParseInterfaces(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels)
        {
            var model = tableModel.Model;
            
            if (model.ModelInstanceInterface == null)
            {
                var interfaceName = $"I{model.CsType.Name}";
                model.SetModelInstanceInterface(new CsTypeDeclaration(interfaceName, model.CsType.Namespace, ModelCsType.Interface));
            }
        }
    }

    public static Option<TableDefinition, IDLOptionFailure> ParseTable(ModelDefinition model)
    {
        if (model == null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Model cannot be null");

        TableDefinition table;

        if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("ITableModel")))
            table = new TableDefinition(model.CsType.Name);
        else if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("IViewModel")))
            table = new ViewDefinition(model.CsType.Name);
        else
            return DLOptionFailure.Fail(DLFailureType.InvalidType, $"Model {model.CsType.Name} is not a valid table or view model.");

        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                table.SetDbName(tableAttribute.Name);

            if (attribute is ViewAttribute viewAttribute)
                table.SetDbName(viewAttribute.Name);

            if (attribute is UseCacheAttribute useCache)
                table.UseCache = useCache.UseCache;

            if (attribute is CacheLimitAttribute cacheLimit)
                table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                table.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (table is ViewDefinition view && attribute is DefinitionAttribute definitionAttribute)
                view.SetDefinition(definitionAttribute.Sql);
        }

        table.SetColumns(model.ValueProperties.Values.Select(table.ParseColumn));

        return table;
    }

    public static void ParseIndices(DatabaseDefinition database)
    {
        var indices = database.TableModels
            .Where(x => !x.IsStub)
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
                        : new List<ColumnDefinition> { column };

                    column.Table.ColumnIndices.Add(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
                }
            }
        }
    }

    public static void ParseRelations(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Where(x => !x.IsStub && x.Table.Type == TableType.Table).Select(x => x.Table))
        {
            var columns = table.Columns.Where(x => x.PrimaryKey).ToList();

            if (!columns.Any())
                throw DLOptionFailure.Exception(DLFailureType.InvalidModel, $"Table {table.DbName} is missing a primary key. Having a primary key for every table is a requirement for DataLinq.");

            table.ColumnIndices.Add(new ColumnIndex($"{table.DbName}_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, columns));
        }

        foreach (var column in database.TableModels.Where(x => !x.IsStub && x.Table.Type == TableType.Table).SelectMany(x => x.Table.Columns.Where(y => y.ForeignKey)))
        {
            foreach (var attribute in column.ValueProperty.Attributes.OfType<ForeignKeyAttribute>())
            {
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

                var relation = new RelationDefinition(attribute.Name, RelationType.OneToMany);
                relation.ForeignKey = CreateRelationPart(relation, foreignKeyIndex, RelationPartType.ForeignKey);
                relation.CandidateKey = CreateRelationPart(relation, candidateKeyIndex, RelationPartType.CandidateKey);

                var candidateProperty = GetRelationProperty(relation.ForeignKey, candidateColumn);
                var columnProperty = GetRelationProperty(relation.CandidateKey, column);

                if (candidateProperty != null && columnProperty != null)
                {
                    foreignKeyIndex.RelationParts.Add(relation.ForeignKey);
                    candidateKeyIndex.RelationParts.Add(relation.CandidateKey);

                    candidateProperty.SetRelationPart(relation.ForeignKey);
                    columnProperty.SetRelationPart(relation.CandidateKey);
                }
            }
        }
    }

    private static RelationPart CreateRelationPart(RelationDefinition relation, ColumnIndex column, RelationPartType type)
    {
        return new RelationPart
        (
            column,
            relation,
            type,
            column.Table.Model.CsType.Name
        );
    }

    private static RelationProperty? GetRelationProperty(RelationPart relationPart, ColumnDefinition column)
    {
        return relationPart.ColumnIndex.Table.Model
            .RelationProperties.Values.SingleOrDefault(x =>
                x.Attributes.Any(y =>
                    y is RelationAttribute relationAttribute
                    && relationAttribute.Table == column.Table.DbName
                    && relationAttribute.Columns[0] == column.DbName
                    && (relationAttribute.Name == null || relationAttribute.Name == relationPart.Relation.ConstraintName)));
    }

    public static RelationProperty AddRelationProperty(ColumnDefinition column, ColumnDefinition referencedColumn, string constraintName)
    {
        var propertyName = referencedColumn.Table.DbName;
        var i = 2;
        while (column.Table.Model.RelationProperties.ContainsKey(propertyName))
            propertyName = propertyName + "_" + i++;

        var relationProperty = new RelationProperty(propertyName, referencedColumn.Table.Model.CsType, column.Table.Model, [new RelationAttribute(referencedColumn.Table.DbName, referencedColumn.DbName, constraintName)]);
        relationProperty.SetRelationName(constraintName);
        relationProperty.Model.AddProperty(relationProperty);

        return relationProperty;
    }

    public static ValueProperty AttachValueProperty(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        var name = capitaliseNames
            ? column.DbName.FirstCharToUpper()
            : column.DbName;

        var type = MetadataTypeConverter.GetType(csTypeName);

        CsTypeDeclaration csType;

        if (type == null)
        {
            if (csTypeName == "enum")
                csType = new CsTypeDeclaration(csTypeName, "", ModelCsType.Enum);
            else
                throw new Exception($"Type {csTypeName} not found.");
        }
        else
        {
            csType = new CsTypeDeclaration(type);
        }


        var property = new ValueProperty(name, csType, column.Table.Model, GetAttributes(column));
        property.SetCsSize(MetadataTypeConverter.CsTypeSize(csTypeName));
        property.SetCsNullable(column.Nullable && MetadataTypeConverter.IsCsTypeNullable(csTypeName));
        //property.SetAttributes(GetAttributes(property));
        property.SetColumn(column);

        column.SetValueProperty(property);
        column.Table.Model.AddProperty(column.ValueProperty);

        return property;
    }

    public static void AttachEnumProperty(ValueProperty property, IEnumerable<(string name, int value)> enumValues, IEnumerable<(string name, int value)> csEnumValues, bool declaredInClass)
    {
        property.SetEnumProperty(new EnumProperty(enumValues.ToList(), csEnumValues.ToList(), declaredInClass));
    }

    public static IEnumerable<Attribute> GetAttributes(ColumnDefinition column)
    {
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
        }
    }

    public static void ParseAttributes(this DatabaseDefinition database)
    {
        foreach (var attribute in database.Attributes)
        {
            if (attribute is DatabaseAttribute databaseAttribute)
                database.SetDbName(databaseAttribute.Name);

            if (attribute is UseCacheAttribute useCache)
                database.SetCache(useCache.UseCache);

            if (attribute is CacheLimitAttribute cacheLimit)
                database.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                database.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (attribute is CacheCleanupAttribute cacheCleanup)
                database.CacheCleanup.Add((cacheCleanup.LimitType, cacheCleanup.Amount));
        }
    }

    public static ColumnDefinition ParseColumn(this TableDefinition table, ValueProperty property)
    {
        var column = new ColumnDefinition(property.PropertyName, table);
        column.SetValueProperty(property);

        foreach (var attribute in property.Attributes)
        {
            if (attribute is ColumnAttribute columnAttribute)
                column.SetDbName(columnAttribute.Name);

            if (attribute is NullableAttribute)
                column.SetNullable();

            //if (attribute is DefaultAttribute defaultAttribute)
            //    column.AddDefaultValue(defaultAttribute.Value);

            if (attribute is AutoIncrementAttribute)
                column.SetAutoIncrement();

            if (attribute is PrimaryKeyAttribute)
                column.SetPrimaryKey();

            if (attribute is ForeignKeyAttribute)
                column.SetForeignKey();

            if (attribute is TypeAttribute t)
                column.AddDbType(new DatabaseColumnType(t.DatabaseType, t.Name, t.Length, t.Decimals, t.Signed));
        }

        return column;
    }
}
