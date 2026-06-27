using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Query;
using Remotion.Linq.Parsing.Structure;

namespace DataLinq.Tests.Compliance;

internal static class CurrentQueryTranslationInspection
{
    // 0.8 Phase 1 migration scaffolding: this intentionally inspects the current
    // Remotion-backed translator. Phase 7 must remove or replace this helper.
    public static Select<TModel> BuildSelect<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var queryParser = QueryParser.CreateDefault();
        var queryModel = queryParser.GetParsedQuery(query.Expression);
        var table = database.Provider.Metadata.TableModels
            .Single(x => x.Model.CsType.Type == typeof(TModel))
            .Table;
        var queryExecutorType = typeof(Database<TDatabase>).Assembly.GetType("DataLinq.Linq.QueryExecutor", throwOnError: true)!;
        var executor = Activator.CreateInstance(
            queryExecutorType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [database.Provider.ReadOnlyAccess, table],
            culture: null)!;
        var parseMethod = queryExecutorType
            .GetMethod("ParseQueryModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(TModel));

        return (Select<TModel>)parseMethod.Invoke(executor, [queryModel])!;
    }

    public static Sql BuildSql<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildSelect(database, query).ToSql();
    }

    public static Select<TModel> BuildLegacySelect<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var queryParser = QueryParser.CreateDefault();
        var queryModel = queryParser.GetParsedQuery(query.Expression);
        var table = database.Provider.Metadata.TableModels
            .Single(x => x.Model.CsType.Type == typeof(TModel))
            .Table;
        var queryExecutorType = typeof(Database<TDatabase>).Assembly.GetType("DataLinq.Linq.QueryExecutor", throwOnError: true)!;
        var executor = Activator.CreateInstance(
            queryExecutorType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [database.Provider.ReadOnlyAccess, table],
            culture: null)!;
        var parseMethod = queryExecutorType
            .GetMethod("ParseLegacyQueryModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(TModel));

        return (Select<TModel>)parseMethod.Invoke(executor, [queryModel])!;
    }

    public static Sql BuildLegacySql<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildLegacySelect(database, query).ToSql();
    }

    public static Select<TModel> BuildPlanSelect<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var plan = RemotionQueryPlanAdapter.Convert(database, query);
        return new QueryPlanSqlBuilder(plan, database.Provider.ReadOnlyAccess).BuildSelect<TModel>();
    }

    public static Sql BuildPlanSql<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildPlanSelect(database, query).ToSql();
    }

    public static Select<TResult> BuildPlanSelect<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var plan = RemotionQueryPlanAdapter.Convert(database, query);
        return new QueryPlanSqlBuilder(plan, database.Provider.ReadOnlyAccess).BuildSelect<TResult>();
    }

    public static Sql BuildPlanSql<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return BuildPlanSelect(database, query).ToSql();
    }

    public static Select<TModel> BuildExpressionPlanSelect<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var plan = ExpressionQueryPlanParser.Convert(database.Provider.Metadata, query.Expression, typeof(TModel));
        return new QueryPlanSqlBuilder(plan, database.Provider.ReadOnlyAccess).BuildSelect<TModel>();
    }

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
