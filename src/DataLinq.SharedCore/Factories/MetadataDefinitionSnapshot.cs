using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Core.Factories;

internal static class MetadataDefinitionSnapshot
{
    public static DatabaseDefinition Copy(DatabaseDefinition source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        var copy = new DatabaseDefinition(source.Name, source.CsType, source.DbName);
        CopyDatabaseState(source, copy);

        var tableMap = new Dictionary<TableDefinition, TableDefinition>();
        var modelMap = new Dictionary<ModelDefinition, ModelDefinition>();
        var columnMap = new Dictionary<ColumnDefinition, ColumnDefinition>();
        var indexMap = new Dictionary<ColumnIndex, ColumnIndex>();
        var relationMap = new Dictionary<RelationDefinition, RelationDefinition>();
        var relationPartMap = new Dictionary<RelationPart, RelationPart>();

        var tableModels = source.TableModels
            .Select(tableModel => CopyTableModel(tableModel, copy, tableMap, modelMap))
            .ToArray();
        copy.SetTableModelsCore(tableModels);

        foreach (var tableModel in source.TableModels)
            CopyValuePropertiesAndColumns(tableModel, tableMap, modelMap, columnMap);

        foreach (var tableModel in source.TableModels)
            CopyColumnIndices(tableModel.Table, tableMap, columnMap, indexMap);

        CopyRelations(source, tableMap, columnMap, indexMap, relationMap, relationPartMap);

        foreach (var tableModel in source.TableModels)
            CopyRelationProperties(tableModel.Model, modelMap, relationPartMap);

        foreach (var tableModel in source.TableModels)
            CopyIndexRelationParts(tableModel.Table, indexMap, relationPartMap);

        return copy;
    }

    private static void CopyDatabaseState(DatabaseDefinition source, DatabaseDefinition destination)
    {
        if (source.CsFile.HasValue)
            destination.SetCsFileCore(source.CsFile.Value);

        if (source.SourceSpan.HasValue)
            destination.SetSourceSpanCore(source.SourceSpan.Value);

        destination.SetAttributesCore(source.Attributes);
        CopyAttributeSpans(source.Attributes, source.GetAttributeSourceLocation, destination.SetAttributeSourceSpanCore);

        destination.SetCacheCore(source.UseCache);
        destination.CacheLimits.AddRangeCore(source.CacheLimits);
        destination.CacheCleanup.AddRangeCore(source.CacheCleanup);
        destination.IndexCache.AddRangeCore(source.IndexCache);
    }

    private static TableModel CopyTableModel(
        TableModel source,
        DatabaseDefinition database,
        Dictionary<TableDefinition, TableDefinition> tableMap,
        Dictionary<ModelDefinition, ModelDefinition> modelMap)
    {
        var model = CopyModel(source.Model);
        var table = CopyTable(source.Table);
        var tableModel = new TableModel(source.CsPropertyName, database, model, table, source.IsStub);

        tableMap.Add(source.Table, table);
        modelMap.Add(source.Model, model);

        return tableModel;
    }

    private static ModelDefinition CopyModel(ModelDefinition source)
    {
        var model = new ModelDefinition(source.CsType);

        if (source.CsFile.HasValue)
            model.SetCsFileCore(source.CsFile.Value);

        if (source.ImmutableType.HasValue)
            model.SetImmutableTypeCore(source.ImmutableType.Value);

        if (source.ImmutableFactory is not null)
            model.SetImmutableFactoryCore(source.ImmutableFactory);

        if (source.MutableType.HasValue)
            model.SetMutableTypeCore(source.MutableType.Value);

        model.SetModelInstanceInterfaceCore(source.ModelInstanceInterface);
        model.SetInterfacesCore(source.OriginalInterfaces);
        model.SetUsingsCore(source.Usings);
        model.SetAttributesCore(source.Attributes);

        if (source.SourceSpan.HasValue)
            model.SetSourceSpanCore(source.SourceSpan.Value);

        CopyAttributeSpans(source.Attributes, source.GetAttributeSourceLocation, model.SetAttributeSourceSpanCore);

        return model;
    }

    private static TableDefinition CopyTable(TableDefinition source)
    {
        var table = source is ViewDefinition
            ? new ViewDefinition(source.DbName)
            : new TableDefinition(source.DbName);

        if (table is ViewDefinition destinationView && source is ViewDefinition { Definition: not null } view)
            destinationView.SetDefinitionCore(view.Definition);

        if (source.explicitUseCache.HasValue)
            table.SetUseCacheCore(source.explicitUseCache.Value);

        table.CacheLimits.AddRangeCore(source.CacheLimits);
        table.IndexCache.AddRangeCore(source.IndexCache);

        return table;
    }

    private static void CopyValuePropertiesAndColumns(
        TableModel source,
        Dictionary<TableDefinition, TableDefinition> tableMap,
        Dictionary<ModelDefinition, ModelDefinition> modelMap,
        Dictionary<ColumnDefinition, ColumnDefinition> columnMap)
    {
        var destinationModel = modelMap[source.Model];
        var destinationTable = tableMap[source.Table];
        var valuePropertyMap = new Dictionary<ValueProperty, ValueProperty>();

        foreach (var sourceProperty in source.Model.ValueProperties.Values)
        {
            var destinationProperty = CopyValueProperty(sourceProperty, destinationModel);
            valuePropertyMap.Add(sourceProperty, destinationProperty);
            destinationModel.AddPropertyCore(destinationProperty);
        }

        var destinationColumns = new List<ColumnDefinition>();
        foreach (var sourceColumn in source.Table.Columns)
        {
            var destinationColumn = CopyColumn(sourceColumn, destinationTable);
            columnMap.Add(sourceColumn, destinationColumn);
            destinationColumns.Add(destinationColumn);

            if (sourceColumn.ValueProperty is not null &&
                valuePropertyMap.TryGetValue(sourceColumn.ValueProperty, out var destinationProperty))
            {
                destinationColumn.SetValuePropertyCore(destinationProperty);
            }
        }

        destinationTable.SetColumnsCore(destinationColumns);

        foreach (var sourceColumn in source.Table.Columns)
        {
            var destinationColumn = columnMap[sourceColumn];

            destinationColumn.SetIndexCore(sourceColumn.Index);
            destinationColumn.SetForeignKeyCore(sourceColumn.ForeignKey);
            destinationColumn.SetAutoIncrementCore(sourceColumn.AutoIncrement);
            destinationColumn.SetNullableCore(sourceColumn.Nullable);
        }

        foreach (var sourcePrimaryKey in source.Table.PrimaryKeyColumns)
            columnMap[sourcePrimaryKey].SetPrimaryKeyCore();
    }

    private static ValueProperty CopyValueProperty(ValueProperty source, ModelDefinition destinationModel)
    {
        var property = new ValueProperty(source.PropertyName, source.CsType, destinationModel, source.Attributes);

        property.SetCsNullableCore(source.CsNullable);
        property.SetCsSizeCore(source.CsSize);

        if (source.SourceInfo.HasValue)
            property.SetSourceInfoCore(source.SourceInfo.Value);

        if (source.EnumProperty.HasValue)
            property.SetEnumPropertyCore(source.EnumProperty.Value);

        CopyAttributeSpans(source.Attributes, source.GetAttributeSourceLocation, property.SetAttributeSourceSpanCore);

        return property;
    }

    private static ColumnDefinition CopyColumn(ColumnDefinition source, TableDefinition destinationTable)
    {
        var column = new ColumnDefinition(source.DbName, destinationTable);

        foreach (var dbType in source.DbTypes)
            column.AddDbTypeCore(dbType.Clone());

        return column;
    }

    private static void CopyColumnIndices(
        TableDefinition source,
        Dictionary<TableDefinition, TableDefinition> tableMap,
        Dictionary<ColumnDefinition, ColumnDefinition> columnMap,
        Dictionary<ColumnIndex, ColumnIndex> indexMap)
    {
        var destinationTable = tableMap[source];

        foreach (var sourceIndex in source.ColumnIndices)
        {
            var destinationIndex = new ColumnIndex(
                sourceIndex.Name,
                sourceIndex.Characteristic,
                sourceIndex.Type,
                sourceIndex.Columns.Select(column => columnMap[column]).ToList());

            destinationTable.ColumnIndices.AddCore(destinationIndex);
            indexMap.Add(sourceIndex, destinationIndex);
        }
    }

    private static void CopyRelations(
        DatabaseDefinition source,
        Dictionary<TableDefinition, TableDefinition> tableMap,
        Dictionary<ColumnDefinition, ColumnDefinition> columnMap,
        Dictionary<ColumnIndex, ColumnIndex> indexMap,
        Dictionary<RelationDefinition, RelationDefinition> relationMap,
        Dictionary<RelationPart, RelationPart> relationPartMap)
    {
        foreach (var sourceRelation in EnumerateRelations(source))
        {
            var destinationRelation = new RelationDefinition(sourceRelation.ConstraintName, sourceRelation.Type);
            destinationRelation.SetOnUpdateCore(sourceRelation.OnUpdate);
            destinationRelation.SetOnDeleteCore(sourceRelation.OnDelete);
            relationMap[sourceRelation] = destinationRelation;
        }

        foreach (var sourceRelation in relationMap.Keys.ToArray())
        {
            var destinationRelation = relationMap[sourceRelation];
            destinationRelation.SetForeignKeyCore(CopyRelationPart(sourceRelation.ForeignKey, tableMap, columnMap, indexMap, relationMap, relationPartMap));
            destinationRelation.SetCandidateKeyCore(CopyRelationPart(sourceRelation.CandidateKey, tableMap, columnMap, indexMap, relationMap, relationPartMap));
        }
    }

    private static IEnumerable<RelationDefinition> EnumerateRelations(DatabaseDefinition source)
    {
        var relations = new List<RelationDefinition>();

        foreach (var relation in source.TableModels
            .SelectMany(tableModel => tableModel.Model.RelationProperties.Values)
            .Select(property => property.RelationPart?.Relation)
            .Concat(source.TableModels
                .SelectMany(tableModel => tableModel.Table.ColumnIndices)
                .SelectMany(index => index.RelationParts)
                .Select(part => part.Relation))
            .Where(relation => relation is not null)
            .Cast<RelationDefinition>())
        {
            if (!relations.Any(existing => ReferenceEquals(existing, relation)))
                relations.Add(relation);
        }

        return relations;
    }

    private static RelationPart CopyRelationPart(
        RelationPart source,
        Dictionary<TableDefinition, TableDefinition> tableMap,
        Dictionary<ColumnDefinition, ColumnDefinition> columnMap,
        Dictionary<ColumnIndex, ColumnIndex> indexMap,
        Dictionary<RelationDefinition, RelationDefinition> relationMap,
        Dictionary<RelationPart, RelationPart> relationPartMap)
    {
        if (relationPartMap.TryGetValue(source, out var existing))
            return existing;

        var part = new RelationPart(
            GetOrCopyColumnIndex(source.ColumnIndex, tableMap, columnMap, indexMap),
            relationMap[source.Relation],
            source.Type,
            source.CsName);

        relationPartMap.Add(source, part);
        return part;
    }

    private static ColumnIndex GetOrCopyColumnIndex(
        ColumnIndex source,
        Dictionary<TableDefinition, TableDefinition> tableMap,
        Dictionary<ColumnDefinition, ColumnDefinition> columnMap,
        Dictionary<ColumnIndex, ColumnIndex> indexMap)
    {
        if (indexMap.TryGetValue(source, out var existing))
            return existing;

        var destinationIndex = new ColumnIndex(
            source.Name,
            source.Characteristic,
            source.Type,
            source.Columns.Select(column => columnMap[column]).ToList());

        tableMap[source.Table].ColumnIndices.AddCore(destinationIndex);
        indexMap.Add(source, destinationIndex);

        return destinationIndex;
    }

    private static void CopyRelationProperties(
        ModelDefinition source,
        Dictionary<ModelDefinition, ModelDefinition> modelMap,
        Dictionary<RelationPart, RelationPart> relationPartMap)
    {
        var destinationModel = modelMap[source];

        foreach (var sourceProperty in source.RelationProperties.Values)
        {
            var destinationProperty = new RelationProperty(
                sourceProperty.PropertyName,
                sourceProperty.CsType,
                destinationModel,
                sourceProperty.Attributes);

            destinationProperty.SetCsNullableCore(sourceProperty.CsNullable);

            if (sourceProperty.SourceInfo.HasValue)
                destinationProperty.SetSourceInfoCore(sourceProperty.SourceInfo.Value);

            if (sourceProperty.RelationName is not null)
                destinationProperty.SetRelationNameCore(sourceProperty.RelationName);

            if (sourceProperty.RelationPart is not null &&
                relationPartMap.TryGetValue(sourceProperty.RelationPart, out var destinationPart))
            {
                destinationProperty.SetRelationPartCore(destinationPart);
            }

            CopyAttributeSpans(sourceProperty.Attributes, sourceProperty.GetAttributeSourceLocation, destinationProperty.SetAttributeSourceSpanCore);
            destinationModel.AddPropertyCore(destinationProperty);
        }
    }

    private static void CopyIndexRelationParts(
        TableDefinition source,
        Dictionary<ColumnIndex, ColumnIndex> indexMap,
        Dictionary<RelationPart, RelationPart> relationPartMap)
    {
        foreach (var sourceIndex in source.ColumnIndices)
        {
            var destinationIndex = indexMap[sourceIndex];

            foreach (var sourcePart in sourceIndex.RelationParts)
                if (relationPartMap.TryGetValue(sourcePart, out var destinationPart) &&
                    !destinationIndex.RelationParts.Contains(destinationPart))
                    destinationIndex.RelationParts.AddCore(destinationPart);
        }
    }

    private static void CopyAttributeSpans(
        IEnumerable<Attribute> attributes,
        Func<Attribute, SourceLocation?> getSourceLocation,
        Action<Attribute, SourceTextSpan> setSourceSpan)
    {
        foreach (var attribute in attributes)
        {
            var sourceLocation = getSourceLocation(attribute);
            if (sourceLocation?.Span is { } sourceSpan)
                setSourceSpan(attribute, sourceSpan);
        }
    }
}
