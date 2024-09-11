using System;
using DataLinq.Query;

namespace DataLinq.Extensions.Helpers;

internal static class QueryExtensions
{
    internal static string ToSql(this Relation relation) => relation switch
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
        _ => throw new NotImplementedException($"Relation {relation} is not supported"),
    };
}