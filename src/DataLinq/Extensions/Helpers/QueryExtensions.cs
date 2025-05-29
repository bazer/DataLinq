using System;
using DataLinq.Query;

namespace DataLinq.Extensions.Helpers;

internal static class QueryExtensions
{
    internal static string ToSql(this Relation relation)
    {
        return relation switch
        {
            Relation.Equal => "=",
            Relation.EqualNull => "IS",
            Relation.NotEqual => "<>",
            Relation.NotEqualNull => "IS NOT",
            Relation.Like => "LIKE",
            Relation.GreaterThan => ">",
            Relation.GreaterThanOrEqual => ">=",
            Relation.LessThan => "<",
            Relation.LessThanOrEqual => "<=",
            Relation.In => "IN",
            Relation.NotIn => "NOT IN",
            Relation.AlwaysFalse => "1=0",
            Relation.AlwaysTrue => "1=1",
            _ => throw new NotImplementedException($"Relation {relation} is not supported for direct SQL conversion."),
        };
    }
}