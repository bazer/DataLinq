using System;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Query;

namespace DataLinq.Tests.Compliance;

internal static class CurrentQueryTranslationInspection
{
    public static Select<TModel> BuildSelect<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var plan = ExpressionQueryPlanParser.Convert(database.Provider.Metadata, query.Expression, typeof(TModel));
        return new QueryPlanSqlBuilder(plan, database.Provider.ReadOnlyAccess).BuildSelect<TModel>();
    }

    public static Sql BuildSql<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildSelect(database, query).ToSql();
    }

    public static Select<TModel> BuildPlanSelect<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
        => BuildSelect(database, query);

    public static Sql BuildPlanSql<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildPlanSelect(database, query).ToSql();
    }

    public static Select<TResult> BuildPlanSelect<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var plan = ExpressionQueryPlanParser.Convert(database.Provider.Metadata, query.Body, typeof(TResult));
        return new QueryPlanSqlBuilder(plan, database.Provider.ReadOnlyAccess).BuildSelect<TResult>();
    }

    public static Sql BuildPlanSql<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildPlanSelect(database, query).ToSql();
    }

    public static Select<TModel> BuildExpressionPlanSelect<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
        => BuildSelect(database, query);

    public static Sql BuildExpressionPlanSql<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildExpressionPlanSelect(database, query).ToSql();
    }

    public static Select<TResult> BuildExpressionPlanSelect<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var plan = ExpressionQueryPlanParser.Convert(database.Provider.Metadata, query.Body, typeof(TResult));
        return new QueryPlanSqlBuilder(plan, database.Provider.ReadOnlyAccess).BuildSelect<TResult>();
    }

    public static Sql BuildExpressionPlanSql<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildExpressionPlanSelect(database, query).ToSql();
    }

    public static string NormalizeSqlWhitespace(string sql)
        => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
