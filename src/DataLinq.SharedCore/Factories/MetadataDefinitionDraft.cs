using System;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public sealed class MetadataDefinitionDraft
{
    private readonly Func<Option<DatabaseDefinition, IDLOptionFailure>> createConstructionGraph;
    private readonly bool ownsConstructionGraph;

    private MetadataDefinitionDraft(
        Func<Option<DatabaseDefinition, IDLOptionFailure>> createConstructionGraph,
        bool ownsConstructionGraph)
    {
        this.createConstructionGraph = createConstructionGraph;
        this.ownsConstructionGraph = ownsConstructionGraph;
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryInputObsoleteMessage)]
    public static MetadataDefinitionDraft FromMutableMetadata(DatabaseDefinition metadata)
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        return new MetadataDefinitionDraft(
            () => metadata,
            ownsConstructionGraph: false);
    }

    public static MetadataDefinitionDraft FromTypedMetadata(MetadataDatabaseDraft metadata)
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        return new MetadataDefinitionDraft(
            () => MetadataTypedDraftConverter.ToConstructionGraph(metadata),
            ownsConstructionGraph: true);
    }

    internal Option<DatabaseDefinition, IDLOptionFailure> TryCreateConstructionGraph()
    {
        var constructionGraph = createConstructionGraph();
        if (!constructionGraph.TryUnwrap(out var metadata, out var constructionGraphFailure))
            return constructionGraphFailure;

        var tableModelValidation = MetadataFactory.ValidateExistingTableModels(metadata);
        if (!tableModelValidation.HasValue)
            return tableModelValidation.Failure;

        var collectionValidation = MetadataFactory.ValidateMetadataCollections(metadata);
        if (!collectionValidation.HasValue)
            return collectionValidation.Failure;

        var csharpSymbolValidation = MetadataFactory.ValidateCSharpSymbolNames(metadata);
        if (!csharpSymbolValidation.HasValue)
            return csharpSymbolValidation.Failure;

        var tableModelPropertyValidation = MetadataFactory.ValidateUniqueTableModelPropertyNames(metadata);
        if (!tableModelPropertyValidation.HasValue)
            return tableModelPropertyValidation.Failure;

        var objectNameValidation = MetadataFactory.ValidateDatabaseObjectNames(metadata);
        if (!objectNameValidation.HasValue)
            return objectNameValidation.Failure;

        var relationalAttributeValidation = MetadataFactory.ValidateRelationalAttributeMetadata(metadata);
        if (!relationalAttributeValidation.HasValue)
            return relationalAttributeValidation.Failure;

        var cacheMetadataValidation = MetadataFactory.ValidateCacheMetadata(metadata);
        if (!cacheMetadataValidation.HasValue)
            return cacheMetadataValidation.Failure;

        var attributeDatabaseTypeValidation = MetadataFactory.ValidateProviderScopedAttributeDatabaseTypes(metadata);
        if (!attributeDatabaseTypeValidation.HasValue)
            return attributeDatabaseTypeValidation.Failure;

        var schemaAnnotationValidation = MetadataFactory.ValidateSchemaAnnotationMetadata(metadata);
        if (!schemaAnnotationValidation.HasValue)
            return schemaAnnotationValidation.Failure;

        var tableNameValidation = MetadataFactory.ValidateUniqueTableNames(metadata);
        if (!tableNameValidation.HasValue)
            return tableNameValidation.Failure;

        var viewDefinitionValidation = MetadataFactory.ValidateViewDefinitions(metadata);
        if (!viewDefinitionValidation.HasValue)
            return viewDefinitionValidation.Failure;

        var primaryKeyValidation = MetadataFactory.ValidateExistingPrimaryKeyColumns(metadata);
        if (!primaryKeyValidation.HasValue)
            return primaryKeyValidation.Failure;

        var indexValidation = MetadataFactory.ValidateExistingColumnIndices(metadata);
        if (!indexValidation.HasValue)
            return indexValidation.Failure;

        var columnPropertyValidation = MetadataFactory.ValidateExistingColumnPropertyBindings(metadata);
        if (!columnPropertyValidation.HasValue)
            return columnPropertyValidation.Failure;

        var columnTypeValidation = MetadataFactory.ValidateExistingColumnTypes(metadata);
        if (!columnTypeValidation.HasValue)
            return columnTypeValidation.Failure;

        var enumValidation = MetadataFactory.ValidateValuePropertyEnums(metadata);
        if (!enumValidation.HasValue)
            return enumValidation.Failure;

        var columnNameValidation = MetadataFactory.ValidateUniqueColumnNames(metadata);
        if (!columnNameValidation.HasValue)
            return columnNameValidation.Failure;

        var defaultValueValidation = MetadataFactory.ValidateValuePropertyDefaults(metadata);
        if (!defaultValueValidation.HasValue)
            return defaultValueValidation.Failure;

        var relationPropertyValidation = MetadataFactory.ValidateExistingRelationPropertyBindings(metadata);
        if (!relationPropertyValidation.HasValue)
            return relationPropertyValidation.Failure;

        var relationValidation = MetadataFactory.ValidateExistingRelationParts(metadata);
        if (!relationValidation.HasValue)
            return relationValidation.Failure;

        return DLOptionFailure.CatchAll(() => ownsConstructionGraph
            ? metadata
            : MetadataDefinitionSnapshot.Copy(metadata));
    }
}
