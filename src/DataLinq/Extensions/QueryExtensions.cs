using DataLinq.Query;

namespace DataLinq.Extensions;

public static class QueryExtensions
{
    public static string ToSql(this Relation relation)
    {
        switch (relation)
        {
            case Relation.Equal:
                return "=";

            case Relation.EqualNull:
                return "IS";

            case Relation.NotEqual:
                return "<>";

            case Relation.NotEqualNull:
                return "IS NOT";

            case Relation.Like:
                return "LIKE";

            case Relation.GreaterThan:
                return ">";

            case Relation.GreaterThanOrEqual:
                return ">=";

            case Relation.LessThan:
                return "<";

            case Relation.LessThanOrEqual:
                return "<=";
        }

        return null;
    }
}