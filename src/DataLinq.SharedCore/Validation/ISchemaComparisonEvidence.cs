using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Validation;

// The shared comparer consumes only evidence about specific unavailable claims.
// Runtime issue collection, logging and host policy stay outside SharedCore.
internal interface ISchemaComparisonEvidence
{
    bool IsColumnOmitted(string table, string column);
    bool IsDefaultOmitted(string table, string column);
    bool IsNullabilityOmitted(string table, string column);
    bool IsIndexOmitted(string table, string index);
    IReadOnlyList<SchemaForeignKeyOmission> GetForeignKeyOmissions(RelationDefinition relation);
}

internal sealed record SchemaForeignKeyOmission(string Identity, ReferentialAction? OnUpdate, ReferentialAction? OnDelete);
