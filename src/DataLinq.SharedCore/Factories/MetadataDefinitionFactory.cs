using System;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public sealed class MetadataDefinitionFactory
{
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

        if (!draft.TryCreateMutableSnapshot().TryUnwrap(out var snapshot, out var snapshotFailure))
            return snapshotFailure;

        return DLOptionFailure.CatchAll(() => BuildCore(snapshot));
    }

    public Option<DatabaseDefinition, IDLOptionFailure> Build(MetadataDatabaseDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        return Build(MetadataDefinitionDraft.FromTypedMetadata(draft));
    }

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

        if (!draft.TryCreateMutableSnapshot().TryUnwrap(out var snapshot, out var snapshotFailure))
            return snapshotFailure;

        return DLOptionFailure.CatchAll(() => BuildProviderMetadataCore(snapshot));
    }

    public Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMetadata(MetadataDatabaseDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        return BuildProviderMetadata(MetadataDefinitionDraft.FromTypedMetadata(draft));
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMetadataCore(DatabaseDefinition draft)
    {
        MetadataFactory.ParseInterfaces(draft);

        return BuildCore(draft);
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildCore(DatabaseDefinition draft)
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

        if (!MetadataFactory.ValidateRelationalAttributeMetadata(draft).TryUnwrap(out _, out var relationalAttributeFailure))
            return relationalAttributeFailure;

        if (!MetadataFactory.ValidateCacheMetadata(draft).TryUnwrap(out _, out var cacheMetadataFailure))
            return cacheMetadataFailure;

        if (!MetadataFactory.ValidateProviderScopedAttributeDatabaseTypes(draft).TryUnwrap(out _, out var attributeDatabaseTypeFailure))
            return attributeDatabaseTypeFailure;

        if (!MetadataFactory.ValidateSchemaAnnotationMetadata(draft).TryUnwrap(out _, out var schemaAnnotationFailure))
            return schemaAnnotationFailure;

        MetadataFactory.NormalizeDatabaseTypeName(draft);

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

        if (!MetadataFactory.ParseIndices(draft).TryUnwrap(out _, out var indexFailure))
            return indexFailure;

        if (!MetadataFactory.ValidateExistingColumnIndices(draft).TryUnwrap(out _, out var indexOwnershipFailure))
            return indexOwnershipFailure;

        if (!MetadataFactory.ValidateExistingRelationParts(draft).TryUnwrap(out _, out var relationPartFailure))
            return relationPartFailure;

        if (!MetadataFactory.ParseRelations(draft).TryUnwrap(out _, out var relationFailure))
            return relationFailure;

        if (!MetadataFactory.ValidateCSharpSymbolNames(draft).TryUnwrap(out _, out var finalizedCsharpSymbolFailure))
            return finalizedCsharpSymbolFailure;

        if (!MetadataFactory.ValidateExistingRelationPropertyBindings(draft).TryUnwrap(out _, out var finalizedRelationPropertyFailure))
            return finalizedRelationPropertyFailure;

        if (!MetadataFactory.ValidateExistingRelationParts(draft).TryUnwrap(out _, out var finalizedRelationPartFailure))
            return finalizedRelationPartFailure;

        MetadataFactory.IndexColumns(draft);

        return draft;
    }
}
