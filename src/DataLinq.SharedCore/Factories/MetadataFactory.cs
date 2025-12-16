using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    public List<string>? Include { get; set; }

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

        if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("ITableModel")/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new TableDefinition(model.CsType.Name);
        else if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("IViewModel")/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new ViewDefinition(model.CsType.Name);
        else
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Model '{model.CsType.Name}' does not inherit from 'ITableModel' or 'IViewModel'.");

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

    public static void IndexColumns(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Select(x => x.Table))
            for (var i = 0; i < table.Columns.Length; i++)
                table.Columns[i].SetIndex(i);
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
                throw DLOptionFailure.Exception(DLFailureType.InvalidModel, $"Table {table.DbName} is missing a primary key.");

            if (!table.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey))
                table.ColumnIndices.Add(new ColumnIndex($"{table.DbName}_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, columns));
        }

        foreach (var foreignKeyColumn in database.TableModels.Where(x => !x.IsStub && x.Table.Type == TableType.Table).SelectMany(x => x.Table.Columns.Where(y => y.ForeignKey)))
        {
            foreach (var attribute in foreignKeyColumn.ValueProperty.Attributes.OfType<ForeignKeyAttribute>())
            {
                var candidateColumn = database
                    .TableModels.FirstOrDefault(x => x.Table.DbName == attribute.Table)?
                    .Table.Columns.FirstOrDefault(x => x.DbName == attribute.Column);

                if (candidateColumn == null) continue;

                var manySideModel = foreignKeyColumn.Table.Model;
                var oneSideModel = candidateColumn.Table.Model;

                var foreignKeyIndex = foreignKeyColumn.ColumnIndices.FirstOrDefault(x => x.Characteristic == IndexCharacteristic.ForeignKey);
                if (foreignKeyIndex == null)
                {
                    foreignKeyIndex = new ColumnIndex(foreignKeyColumn.DbName, IndexCharacteristic.ForeignKey, IndexType.BTREE, [foreignKeyColumn]);
                    foreignKeyColumn.Table.ColumnIndices.Add(foreignKeyIndex);
                }

                var candidateKeyIndex = candidateColumn.Table.ColumnIndices.First(x => x.Characteristic == IndexCharacteristic.PrimaryKey);

                var relation = new RelationDefinition(attribute.Name, RelationType.OneToMany);
                var manySidePart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "");
                var oneSidePart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, "");
                relation.ForeignKey = manySidePart;
                relation.CandidateKey = oneSidePart;

                // --- Link or Create Many-to-One Property ---
                var manyToOneProp = GetRelationProperty(manySideModel, oneSideModel.Table.DbName, candidateColumn.DbName, attribute.Name);
                if (manyToOneProp != null)
                {
                    manyToOneProp.SetRelationPart(manySidePart);
                    if (!manySidePart.ColumnIndex.RelationParts.Contains(manySidePart))
                        manySidePart.ColumnIndex.RelationParts.Add(manySidePart);
                }
                else
                {
                    var propName = Regex.Replace(foreignKeyColumn.DbName, "(_id|id|fk)$", "", RegexOptions.IgnoreCase).ToPascalCase();
                    var propType = oneSideModel.CsType;
                    var propAttr = new RelationAttribute(oneSideModel.Table.DbName, candidateColumn.DbName, attribute.Name);
                    AddRelationProperty(manySideModel, propName, propType, manySidePart, propAttr);
                }

                // --- Link or Create One-to-Many Property ---
                var oneToManyProp = GetRelationProperty(oneSideModel, manySideModel.Table.DbName, foreignKeyColumn.DbName, attribute.Name);
                if (oneToManyProp != null)
                {
                    oneToManyProp.SetRelationPart(oneSidePart);
                    if (!oneSidePart.ColumnIndex.RelationParts.Contains(oneSidePart))
                        oneSidePart.ColumnIndex.RelationParts.Add(oneSidePart);
                }
                else
                {
                    var propName = manySideModel.CsType.Name;
                    var genericTypeName = manySideModel.CsType.Name;
                    var propType = new CsTypeDeclaration($"IImmutableRelation<{genericTypeName}>", "DataLinq.Instances", ModelCsType.Interface);
                    var propAttr = new RelationAttribute(manySideModel.Table.DbName, foreignKeyColumn.DbName, attribute.Name);
                    AddRelationProperty(oneSideModel, propName, propType, oneSidePart, propAttr);
                }
            }
        }
    }

    private static RelationProperty? GetRelationProperty(ModelDefinition model, string referencedTableName, string referencedColumnName, string constraintName)
    {
        // Find a property in the model that has a [Relation] attribute matching the target table, column, and constraint name.
        return model.RelationProperties.Values.SingleOrDefault(p =>
            p.Attributes.OfType<RelationAttribute>().Any(a =>
                a.Table == referencedTableName &&
                a.Columns.Contains(referencedColumnName) && // Check if the column is in the list
                (a.Name == null || a.Name == constraintName)
            )
        );
    }

    public static void AddRelationProperty(ModelDefinition model, string propertyName, CsTypeDeclaration propertyType, RelationPart relationPart, RelationAttribute relationAttribute)
    {
        var originalPropertyName = propertyName;
        var i = 2;
        while (model.RelationProperties.ContainsKey(propertyName) || model.ValueProperties.ContainsKey(propertyName))
        {
            propertyName = $"{originalPropertyName}_{i++}";
        }

        var relationProperty = new RelationProperty(propertyName, propertyType, model, [relationAttribute]);
        relationProperty.SetRelationName(relationAttribute.Name);
        relationProperty.SetRelationPart(relationPart); // Directly link the part
        model.AddProperty(relationProperty);

        // Also ensure the back-reference on the index is set
        if (!relationPart.ColumnIndex.RelationParts.Contains(relationPart))
        {
            relationPart.ColumnIndex.RelationParts.Add(relationPart);
        }
    }

    public static ValueProperty AttachValueProperty(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        var name = capitaliseNames && !column.DbName.IsFirstCharUpper()
            ? column.DbName.ToPascalCase()
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
        property.SetCsNullable(column.Nullable); // && MetadataTypeConverter.IsCsTypeNullable(csTypeName));
        //property.SetAttributes(GetAttributes(property));
        property.SetColumn(column);

        column.SetValueProperty(property);
        column.Table.Model.AddProperty(column.ValueProperty);

        return property;
    }

    //public static void AttachEnumProperty(ValueProperty property, IEnumerable<(string name, int value)> enumValues, bool declaredInClass)
    //{
    //    property.SetEnumProperty(new EnumProperty(enumValues, enumValues, declaredInClass));
    //}

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