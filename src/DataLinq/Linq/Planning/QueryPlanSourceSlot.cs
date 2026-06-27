using System;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal sealed record QueryPlanSourceSlot(
    string Id,
    string Alias,
    TableDefinition Table,
    Type ElementType,
    QueryPlanSourceKind Kind,
    QueryPlanSourceCardinality Cardinality,
    bool IsNullable);

internal enum QueryPlanSourceKind
{
    RootTable,
    ExplicitJoin,
    RelationSubquery
}

internal enum QueryPlanSourceCardinality
{
    Many,
    One,
    ZeroOrOne
}
