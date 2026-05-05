using System;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public sealed class MetadataDefinitionFactory
{
    public Option<DatabaseDefinition, IDLOptionFailure> Build(DatabaseDefinition draft) =>
        DLOptionFailure.CatchAll(() => BuildCore(draft));

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildCore(DatabaseDefinition draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Metadata draft cannot be null.");

        if (!MetadataFactory.ValidateUniqueTableNames(draft).TryUnwrap(out _, out var duplicateTableFailure))
            return duplicateTableFailure;

        if (!MetadataFactory.ValidateUniqueColumnNames(draft).TryUnwrap(out _, out var duplicateColumnFailure))
            return duplicateColumnFailure;

        if (!MetadataFactory.ParseIndices(draft).TryUnwrap(out _, out var indexFailure))
            return indexFailure;

        if (!MetadataFactory.ParseRelations(draft).TryUnwrap(out _, out var relationFailure))
            return relationFailure;

        MetadataFactory.IndexColumns(draft);

        return draft;
    }
}
