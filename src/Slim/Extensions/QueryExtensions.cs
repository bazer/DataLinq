using Modl.Db.Query;

namespace Slim.Extensions
{
    public static class QueryExtensions
    {
        public static string ToSql(this Relation relation)
        {
            if (relation == Relation.Equal)
                return "=";
            else if (relation == Modl.Db.Query.Relation.NotEqual)
                return "<>";
            else if (relation == Modl.Db.Query.Relation.Like)
                return "LIKE";
            else if (relation == Modl.Db.Query.Relation.BiggerThan)
                return ">";
            else if (relation == Modl.Db.Query.Relation.BiggerThanOrEqual)
                return ">=";
            else if (relation == Modl.Db.Query.Relation.SmallerThan)
                return "<";
            else if (relation == Modl.Db.Query.Relation.SmallerThanOrEqual)
                return "<=";

            return null;
        }
    }
}