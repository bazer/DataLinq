using System.Linq;
using System.Threading.Tasks;
using DataLinq.Query;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesSqlBuilderTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_SimpleWhereRendersExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_SimpleWhereRendersExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);
        var sql = databaseScope.Database
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .SelectQuery()
            .ToSql();

        await AssertSql(
            sql,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0",
            (1, $"{parameterSign}w0", "d005"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_SimpleWhereAndRendersExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_SimpleWhereAndRendersExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);
        var sql = new SqlQuery("departments", databaseScope.Database.Transaction())
            .Where("dept_no").EqualTo("d005")
            .And("dept_name").EqualTo("Development")
            .And("dept_name").EqualTo("Development")
            .SelectQuery()
            .ToSql();

        await AssertSql(
            sql,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0 AND {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w1 AND {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w2",
            (3, $"{parameterSign}w1", "Development"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_SimpleWhereOrRendersExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_SimpleWhereOrRendersExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);
        var sql = new SqlQuery("departments", databaseScope.Database.Transaction())
            .Where("dept_no").EqualTo("d005")
            .Or("dept_name").EqualTo("Development")
            .Or("dept_name").EqualTo("Development")
            .SelectQuery()
            .ToSql();

        await AssertSql(
            sql,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0 OR {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w1 OR {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w2",
            (3, $"{parameterSign}w1", "Development"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_ComplexWhereOrRendersExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_ComplexWhereOrRendersExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);
        var sql = new SqlQuery("departments", databaseScope.Database.Transaction())
            .Where(x => x.Where("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
            .Or(x => x.Where("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
            .SelectQuery()
            .ToSql();

        await AssertSql(
            sql,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
({escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0 AND {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w1) OR ({escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w2 AND {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w3)",
            (4, $"{parameterSign}w3", "Development"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_ComplexWhereAndRendersExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_ComplexWhereAndRendersExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);
        var sql = new SqlQuery("departments", databaseScope.Database.Transaction())
            .Where(x => x.Where("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
            .And(x => x.Where("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
            .SelectQuery()
            .ToSql();

        await AssertSql(
            sql,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
({escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0 AND {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w1) AND ({escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w2 AND {escapeCharacter}dept_name{escapeCharacter} = {parameterSign}w3)",
            (4, $"{parameterSign}w1", "Marketing"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_ComparisonPredicatesRenderExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_ComparisonPredicatesRenderExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);

        var notEqual = databaseScope.Database.From("departments").Where("dept_no").NotEqualTo("d005").SelectQuery().ToSql();
        var greaterThan = databaseScope.Database.From("departments").Where("dept_no").GreaterThan("d005").SelectQuery().ToSql();
        var greaterThanOrEqual = databaseScope.Database.From("departments").Where("dept_no").GreaterThanOrEqual("d005").SelectQuery().ToSql();
        var lessThan = databaseScope.Database.From("departments").Where("dept_no").LessThan("d005").SelectQuery().ToSql();
        var lessThanOrEqual = databaseScope.Database.From("departments").Where("dept_no").LessThanOrEqual("d005").SelectQuery().ToSql();
        var like = databaseScope.Database.From("departments").Where("dept_no").Like("d005%").SelectQuery().ToSql();

        await AssertSql(
            notEqual,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} <> {parameterSign}w0",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(
            greaterThan,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} > {parameterSign}w0",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(
            greaterThanOrEqual,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} >= {parameterSign}w0",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(
            lessThan,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} < {parameterSign}w0",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(
            lessThanOrEqual,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} <= {parameterSign}w0",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(
            like,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} LIKE {parameterSign}w0",
            (1, $"{parameterSign}w0", "d005%"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_WhereNotRendersExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_WhereNotRendersExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);
        var sql = databaseScope.Database
            .From("departments")
            .WhereNot("dept_no").EqualTo("d005")
            .SelectQuery()
            .ToSql();

        await AssertSql(
            sql,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
NOT ({escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0)",
            (1, $"{parameterSign}w0", "d005"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_OrderByVariantsRenderExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_OrderByVariantsRenderExpectedSql));

        var (_, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);

        var orderBy = databaseScope.Database.From("departments").OrderBy("dept_no").SelectQuery().ToSql();
        var orderByDesc = databaseScope.Database.From("departments").OrderByDesc("dept_no").SelectQuery().ToSql();
        var orderByTwice = databaseScope.Database.From("departments").OrderBy("dept_no").OrderBy("dept_name").SelectQuery().ToSql();
        var orderByDescTwice = databaseScope.Database.From("departments").OrderByDesc("dept_no").OrderByDesc("dept_name").SelectQuery().ToSql();
        var orderByMixed = databaseScope.Database.From("departments").OrderBy("dept_no").OrderByDesc("dept_name").SelectQuery().ToSql();
        var orderByDescMixed = databaseScope.Database.From("departments").OrderByDesc("dept_no").OrderBy("dept_name").SelectQuery().ToSql();

        await AssertSql(orderBy, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
ORDER BY {escapeCharacter}dept_no{escapeCharacter}");
        await AssertSql(orderByDesc, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
ORDER BY {escapeCharacter}dept_no{escapeCharacter} DESC");
        await AssertSql(orderByTwice, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
ORDER BY {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter}");
        await AssertSql(orderByDescTwice, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
ORDER BY {escapeCharacter}dept_no{escapeCharacter} DESC, {escapeCharacter}dept_name{escapeCharacter} DESC");
        await AssertSql(orderByMixed, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
ORDER BY {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} DESC");
        await AssertSql(orderByDescMixed, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
ORDER BY {escapeCharacter}dept_no{escapeCharacter} DESC, {escapeCharacter}dept_name{escapeCharacter}");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_WhereOrderLimitVariantsRenderExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_WhereOrderLimitVariantsRenderExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);

        var whereOrderBy = databaseScope.Database.From("departments").Where("dept_no").EqualTo("d005").OrderBy("dept_no").SelectQuery().ToSql();
        var whereOrderByDesc = databaseScope.Database.From("departments").Where("dept_no").EqualTo("d005").OrderByDesc("dept_no").SelectQuery().ToSql();
        var limit1 = databaseScope.Database.From("departments").Limit(1).SelectQuery().ToSql();
        var limit2 = databaseScope.Database.From("departments").Limit(2).SelectQuery().ToSql();
        var limit2Offset5 = databaseScope.Database.From("departments").Limit(2, 5).SelectQuery().ToSql();
        var whereOrderLimit = databaseScope.Database.From("departments").Where("dept_no").EqualTo("d005").OrderByDesc("dept_no").Limit(1).SelectQuery().ToSql();
        var whereLimitOrder = databaseScope.Database.From("departments").Where("dept_no").EqualTo("d005").Limit(1).OrderByDesc("dept_no").SelectQuery().ToSql();

        await AssertSql(
            whereOrderBy,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0
ORDER BY {escapeCharacter}dept_no{escapeCharacter}",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(
            whereOrderByDesc,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0
ORDER BY {escapeCharacter}dept_no{escapeCharacter} DESC",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(limit1, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
LIMIT 1");
        await AssertSql(limit2, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
LIMIT 2");
        await AssertSql(limit2Offset5, $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
LIMIT 2 OFFSET 5");
        await AssertSql(
            whereOrderLimit,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0
ORDER BY {escapeCharacter}dept_no{escapeCharacter} DESC
LIMIT 1",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(
            whereLimitOrder,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0
ORDER BY {escapeCharacter}dept_no{escapeCharacter} DESC
LIMIT 1",
            (1, $"{parameterSign}w0", "d005"));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_WhatJoinInsertAndInClausesRenderExpectedSql(TestProviderDescriptor provider)
    {
        using var databaseScope = OpenDatabase(provider, nameof(SqlBuilder_WhatJoinInsertAndInClausesRenderExpectedSql));

        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(databaseScope.Database);
        var lastInsertCommand = databaseScope.Database.Provider.Constants.LastInsertCommand;

        var what = databaseScope.Database.From("departments").What("dept_name").SelectQuery().ToSql();
        var explicitJoin = databaseScope.Database
            .From("departments", "d")
            .Join("dept_manager", "m").On(on => on.Where("dept_no", "d").EqualToColumn("dept_no", "m"))
            .SelectQuery()
            .ToSql();
        var includedJoin = databaseScope.Database
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .SelectQuery()
            .ToSql();
        var includedJoinWhere = databaseScope.Database
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .Where("m.dept_no").EqualTo("d005")
            .Limit(1)
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();
        var includedJoinLimit = databaseScope.Database
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .Limit(1)
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();
        var includedJoinOrder = databaseScope.Database
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();
        var doubleJoin = databaseScope.Database
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .Join("dept-emp e").On(on => on.Where("e.dept_no").EqualToColumn("m.dept_no"))
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();
        var insert = databaseScope.Database.From("departments").Set("dept_no", "d005").InsertQuery().ToSql();
        var insertWithLastId = databaseScope.Database.From("departments").Set("dept_no", "d005").AddLastIdQuery().InsertQuery().ToSql();

        var oneId = new[] { 3 };
        var manyIds = new[] { 1, 2, 3 };
        var inOne = databaseScope.Database.From("departments d").Where("Id").In(oneId).SelectQuery().ToSql();
        var inMany = databaseScope.Database.From("departments d").Where("Id").In(manyIds).SelectQuery().ToSql();
        var notInMany = databaseScope.Database.From("departments").Where("Id").NotIn(manyIds).SelectQuery().ToSql();

        await AssertSql(what, $"SELECT {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}");
        await AssertSql(explicitJoin, $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
JOIN {databasePrefix}{escapeCharacter}dept_manager{escapeCharacter} m ON d.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}");
        await AssertSql(includedJoin, $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
JOIN {databasePrefix}{escapeCharacter}dept_manager{escapeCharacter} m ON d.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}");
        await AssertSql(
            includedJoinWhere,
            $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
JOIN {databasePrefix}{escapeCharacter}dept_manager{escapeCharacter} m ON d.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}
WHERE
m.{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0
ORDER BY d.{escapeCharacter}dept_no{escapeCharacter} DESC
LIMIT 1",
            (1, $"{parameterSign}w0", "d005"));
        await AssertSql(includedJoinLimit, $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
JOIN {databasePrefix}{escapeCharacter}dept_manager{escapeCharacter} m ON d.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}
ORDER BY d.{escapeCharacter}dept_no{escapeCharacter} DESC
LIMIT 1");
        await AssertSql(includedJoinOrder, $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
JOIN {databasePrefix}{escapeCharacter}dept_manager{escapeCharacter} m ON d.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}
ORDER BY d.{escapeCharacter}dept_no{escapeCharacter} DESC");
        await AssertSql(doubleJoin, $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
JOIN {databasePrefix}{escapeCharacter}dept_manager{escapeCharacter} m ON d.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}
JOIN {databasePrefix}{escapeCharacter}dept-emp{escapeCharacter} e ON e.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}
ORDER BY d.{escapeCharacter}dept_no{escapeCharacter} DESC");
        await AssertSql(
            insert,
            $@"INSERT INTO {databasePrefix}{escapeCharacter}departments{escapeCharacter} ({escapeCharacter}dept_no{escapeCharacter}) VALUES ({parameterSign}v0)",
            (1, $"{parameterSign}v0", "d005"));
        await AssertSql(
            insertWithLastId,
            $@"INSERT INTO {databasePrefix}{escapeCharacter}departments{escapeCharacter} ({escapeCharacter}dept_no{escapeCharacter}) VALUES ({parameterSign}v0);
SELECT {lastInsertCommand}",
            (1, $"{parameterSign}v0", "d005"));
        await AssertSql(
            inOne,
            $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
WHERE
{escapeCharacter}Id{escapeCharacter} IN ({parameterSign}w0)",
            (1, $"{parameterSign}w0", 3));
        await AssertSql(
            inMany,
            $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
WHERE
{escapeCharacter}Id{escapeCharacter} IN ({parameterSign}w0, {parameterSign}w1, {parameterSign}w2)",
            (3, $"{parameterSign}w2", 3));
        await AssertSql(
            notInMany,
            $@"SELECT {escapeCharacter}dept_no{escapeCharacter}, {escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter}
WHERE
{escapeCharacter}Id{escapeCharacter} NOT IN ({parameterSign}w0, {parameterSign}w1, {parameterSign}w2)",
            (3, $"{parameterSign}w1", 2));
    }

    private static EmployeesTestDatabase OpenDatabase(TestProviderDescriptor provider, string scenarioName)
        => EmployeesTestDatabase.OpenSharedSeeded(provider, scenarioName, EmployeesSeedMode.None);

    private static (string parameterSign, string escapeCharacter, string databasePrefix) GetSqlConstants(Database<EmployeesDb> database)
    {
        var constants = database.Provider.Constants;
        var databasePrefix = constants.SupportsMultipleDatabases
            ? $"{constants.EscapeCharacter}{database.Provider.DatabaseName}{constants.EscapeCharacter}."
            : string.Empty;

        return (
            constants.ParameterSign,
            constants.EscapeCharacter,
            databasePrefix);
    }

    private static async Task AssertSql(Sql sql, string expectedSql, (int count, string? parameterName, object? parameterValue)? parameterExpectation = null)
    {
        await Assert.That(sql.Text).IsEqualTo(expectedSql);

        if (parameterExpectation is null)
        {
            await Assert.That(sql.Parameters.Count).IsEqualTo(0);
            return;
        }

        await Assert.That(sql.Parameters.Count).IsEqualTo(parameterExpectation.Value.count);

        if (parameterExpectation.Value.parameterName is not null)
        {
            var parameter = sql.Parameters.FirstOrDefault(x => x.ParameterName == parameterExpectation.Value.parameterName);
            await Assert.That(parameter).IsNotNull();
            await Assert.That(parameter!.Value).IsEqualTo(parameterExpectation.Value.parameterValue);
        }
    }
}
