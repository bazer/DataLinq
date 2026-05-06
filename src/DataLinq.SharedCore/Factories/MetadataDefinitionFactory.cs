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

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMetadataCore(DatabaseDefinition draft)
    {
        MetadataFactory.ParseInterfaces(draft);

        return BuildCore(draft);
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildCore(DatabaseDefinition draft)
    {
        if (!MetadataFactory.ValidateUniqueTableNames(draft).TryUnwrap(out _, out var duplicateTableFailure))
            return duplicateTableFailure;

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

        if (!MetadataFactory.ValidateExistingRelationParts(draft).TryUnwrap(out _, out var finalizedRelationPartFailure))
            return finalizedRelationPartFailure;

        MetadataFactory.IndexColumns(draft);

        return draft;
    }
}
