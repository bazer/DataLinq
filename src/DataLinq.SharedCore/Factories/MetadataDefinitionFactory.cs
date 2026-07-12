using System;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public sealed class MetadataDefinitionFactory
{
    public MetadataDefinitionFactory()
    {
    }

    // Kept for source compatibility. Generated-name diagnostics are emitted after source transforms.
    public MetadataDefinitionFactory(Action<string>? log)
    {
        _ = log;
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryInputObsoleteMessage)]
    public Option<DatabaseDefinition, IDLOptionFailure> Build(DatabaseDefinition draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        return Build(MetadataDefinitionDraft.FromMutableMetadata(draft));
    }

    public Option<DatabaseDefinition, IDLOptionFailure> Build(MetadataDefinitionDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        if (!draft.TryCreateConstructionGraph().TryUnwrap(out var constructionGraph, out var constructionGraphFailure))
            return constructionGraphFailure;

        return DLOptionFailure.CatchAll(() => BuildCore(constructionGraph, GuidStorageResolutionMode.Model));
    }

    public Option<DatabaseDefinition, IDLOptionFailure> Build(MetadataDatabaseDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        if (!MetadataTypedDraftConverter.ToConstructionGraph(draft).TryUnwrap(out var constructionGraph, out var constructionGraphFailure))
            return constructionGraphFailure;

        return DLOptionFailure.CatchAll(() => BuildCore(constructionGraph, GuidStorageResolutionMode.Model));
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryInputObsoleteMessage)]
    public Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMetadata(DatabaseDefinition draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        return BuildProviderMetadata(MetadataDefinitionDraft.FromMutableMetadata(draft));
    }

    public Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMetadata(MetadataDefinitionDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        if (!draft.TryCreateConstructionGraph().TryUnwrap(out var constructionGraph, out var constructionGraphFailure))
            return constructionGraphFailure;

        return DLOptionFailure.CatchAll(() => BuildProviderMetadataCore(constructionGraph));
    }

    public Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMetadata(MetadataDatabaseDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        if (!MetadataTypedDraftConverter.ToConstructionGraph(draft).TryUnwrap(out var constructionGraph, out var constructionGraphFailure))
            return constructionGraphFailure;

        return DLOptionFailure.CatchAll(() => BuildProviderMetadataCore(constructionGraph));
    }

    internal Option<DatabaseDefinition, IDLOptionFailure> BuildDeferredSourceMetadata(
        MetadataDatabaseDraft draft)
        => BuildSourceMetadata(draft, GuidStorageResolutionMode.DeferredSource);

    private Option<DatabaseDefinition, IDLOptionFailure> BuildSourceMetadata(
        MetadataDatabaseDraft draft,
        GuidStorageResolutionMode guidStorageResolutionMode)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        if (!MetadataTypedDraftConverter.ToConstructionGraph(draft)
            .TryUnwrap(out var constructionGraph, out var constructionGraphFailure))
            return constructionGraphFailure;

        return DLOptionFailure.CatchAll(() =>
            BuildCore(constructionGraph, guidStorageResolutionMode));
    }

    private Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMetadataCore(DatabaseDefinition draft)
    {
        MetadataFactory.ParseInterfacesCore(draft);

        return BuildCore(draft, GuidStorageResolutionMode.ProviderSnapshot);
    }

    private Option<DatabaseDefinition, IDLOptionFailure> BuildCore(
        DatabaseDefinition draft,
        GuidStorageResolutionMode guidStorageResolutionMode)
    {
        if (!MetadataFactory.ValidateExistingTableModels(draft).TryUnwrap(out _, out var tableModelFailure))
            return tableModelFailure;

        if (!MetadataFactory.ValidateMetadataCollections(draft).TryUnwrap(out _, out var collectionFailure))
            return collectionFailure;

        if (!MetadataFactory.ValidateCSharpSymbolNames(draft).TryUnwrap(out _, out var csharpSymbolFailure))
            return csharpSymbolFailure;

        if (!MetadataFactory.ValidateUniqueTableModelPropertyNames(draft).TryUnwrap(out _, out var duplicateTableModelPropertyFailure))
            return duplicateTableModelPropertyFailure;

        if (!MetadataFactory.ValidateDatabaseObjectNames(draft).TryUnwrap(out _, out var objectNameFailure))
            return objectNameFailure;

        if (!MetadataFactory.ValidateIdentityAttributeMetadata(draft).TryUnwrap(out _, out var identityAttributeFailure))
            return identityAttributeFailure;

        if (!MetadataFactory.ValidateRelationalAttributeMetadata(draft).TryUnwrap(out _, out var relationalAttributeFailure))
            return relationalAttributeFailure;

        if (!MetadataFactory.ValidateCacheMetadata(draft).TryUnwrap(out _, out var cacheMetadataFailure))
            return cacheMetadataFailure;

        if (!MetadataFactory.ValidateProviderScopedAttributeDatabaseTypes(draft).TryUnwrap(out _, out var attributeDatabaseTypeFailure))
            return attributeDatabaseTypeFailure;

        if (!MetadataFactory.ValidateGuidStorageAttributeMetadata(draft).TryUnwrap(out _, out var guidStorageFailure))
            return guidStorageFailure;

        if (!MetadataFactory.ValidateSchemaAnnotationMetadata(draft).TryUnwrap(out _, out var schemaAnnotationFailure))
            return schemaAnnotationFailure;

        MetadataFactory.NormalizeDatabaseTypeNameCore(draft);

        if (!MetadataFactory.ValidateUniqueTableNames(draft).TryUnwrap(out _, out var duplicateTableFailure))
            return duplicateTableFailure;

        if (!MetadataFactory.ValidateViewDefinitions(draft).TryUnwrap(out _, out var viewDefinitionFailure))
            return viewDefinitionFailure;

        if (!MetadataFactory.ValidateExistingColumnPropertyBindings(draft).TryUnwrap(out _, out var columnPropertyFailure))
            return columnPropertyFailure;

        if (!MetadataFactory.ValidateExistingColumnTypes(draft).TryUnwrap(out _, out var columnTypeFailure))
            return columnTypeFailure;

        if (!MetadataFactory.ValidateValuePropertyEnums(draft).TryUnwrap(out _, out var enumFailure))
            return enumFailure;

        if (!MetadataFactory.ValidateValuePropertyDefaults(draft).TryUnwrap(out _, out var defaultValueFailure))
            return defaultValueFailure;

        if (!MetadataFactory.ValidateExistingRelationPropertyBindings(draft).TryUnwrap(out _, out var relationPropertyFailure))
            return relationPropertyFailure;

        if (!MetadataFactory.ValidateExistingPrimaryKeyColumns(draft).TryUnwrap(out _, out var primaryKeyFailure))
            return primaryKeyFailure;

        if (!MetadataFactory.ValidateUniqueColumnNames(draft).TryUnwrap(out _, out var duplicateColumnFailure))
            return duplicateColumnFailure;

        if (!MetadataFactory.ParseIndicesCore(draft).TryUnwrap(out _, out var indexFailure))
            return indexFailure;

        if (!MetadataFactory.ValidateExistingColumnIndices(draft).TryUnwrap(out _, out var indexOwnershipFailure))
            return indexOwnershipFailure;

        if (!MetadataFactory.ValidateExistingRelationParts(draft).TryUnwrap(out _, out var relationPartFailure))
            return relationPartFailure;

        if (!MetadataFactory.ParseRelationsCore(draft, out var generatedRelationProperties).TryUnwrap(out _, out var relationFailure))
            return relationFailure;

        if (generatedRelationProperties &&
            !MetadataFactory.ValidateCSharpSymbolNames(draft).TryUnwrap(out _, out var finalizedCsharpSymbolFailure))
            return finalizedCsharpSymbolFailure;

        if (!MetadataFactory.ValidateExistingRelationPropertyBindings(draft).TryUnwrap(out _, out var finalizedRelationPropertyFailure))
            return finalizedRelationPropertyFailure;

        if (!MetadataFactory.ValidateExistingRelationParts(draft).TryUnwrap(out _, out var finalizedRelationPartFailure))
            return finalizedRelationPartFailure;

        if (!MetadataFactory.ResolveGuidStorageDefinitionsCore(draft, guidStorageResolutionMode)
            .TryUnwrap(out _, out var guidStorageResolutionFailure))
            return guidStorageResolutionFailure;

        MetadataFactory.IndexColumnsCore(draft);
        draft.Freeze();

        return draft;
    }
}
