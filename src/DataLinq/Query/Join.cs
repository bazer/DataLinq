using System;

namespace DataLinq.Query
{
    internal enum JoinType
    {
        Inner,
        LeftOuter,
        RightOuter
    }

    public class Join<T>
    {
        public readonly SqlQuery<T> Query;
        readonly string TableName;
        readonly JoinType Type;
        protected WhereGroup<T> WhereContainer;
        private readonly string? Alias;

        internal string DbName => string.IsNullOrEmpty(Alias)
            ? TableName
            : $"{TableName} {Alias}";

        internal Join(SqlQuery<T> query, string tableName, string? alias, JoinType type)
        {
            this.Query = query;
            this.TableName = tableName;
            this.Type = type;
            this.Alias = alias;
        }

        public Where<T> On(string columnName, string alias = null)
        {
            if (WhereContainer == null)
                WhereContainer = new WhereGroup<T>(Query);

            if (alias == null)
                (columnName, alias) = QueryUtils.ParseColumnNameAndAlias(columnName);

            return WhereContainer.AddWhere(columnName, alias, BooleanType.And);
        }

        public Sql GetSql(Sql sql, string paramPrefix)
        {
            if (Type == JoinType.Inner)
                sql.AddText("\nJOIN ");
            else if (Type == JoinType.LeftOuter)
                sql.AddText("\nLEFT JOIN ");
            else if (Type == JoinType.RightOuter)
                sql.AddText("\nRIGHT JOIN ");
            else
                throw new NotImplementedException("Wrong JoinType: " + Type);

            sql.AddFormat("{0} ON ", DbName);

            WhereContainer.AddCommandString(sql, paramPrefix, true);

            return sql;
        }
    }
}
