using Slim.Query;

namespace Slim.Extensions
{
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

                case Relation.BiggerThan:
                    return ">";

                case Relation.BiggerThanOrEqual:
                    return ">=";

                case Relation.SmallerThan:
                    return "<";

                case Relation.SmallerThanOrEqual:
                    return "<=";
            }

            return null;
        }
    }
}