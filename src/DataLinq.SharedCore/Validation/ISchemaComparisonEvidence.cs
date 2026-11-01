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
    int GetForeignKeyOmissionCount(RelationDefinition relation);
}
