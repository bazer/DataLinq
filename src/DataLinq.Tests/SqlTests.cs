using DataLinq.Query;
using DataLinq.Tests.Models;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests;

public class SqlTests : BaseTests
{
    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhere(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .SelectQuery()
            .ToSql();


        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} = {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereAnd(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where("dept_no").EqualTo("d005")
            .And("dept_name").EqualTo("Development")
            .And("dept_name").EqualTo("Development")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} = {sign}w0 AND {escape}dept_name{escape} = {sign}w1 AND {escape}dept_name{escape} = {sign}w2", sql.Text);
        Assert.Equal(3, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Development", sql.Parameters[1].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOr(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where("dept_no").EqualTo("d005")
            .Or("dept_name").EqualTo("Development")
            .Or("dept_name").EqualTo("Development")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} = {sign}w0 OR {escape}dept_name{escape} = {sign}w1 OR {escape}dept_name{escape} = {sign}w2", sql.Text);
        Assert.Equal(3, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Development", sql.Parameters[1].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ComplexWhereOR(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where(x => x.Where("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
            .Or(x => x.Where("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
({escape}dept_no{escape} = {sign}w0 AND {escape}dept_name{escape} = {sign}w1) OR ({escape}dept_no{escape} = {sign}w2 AND {escape}dept_name{escape} = {sign}w3)", sql.Text);
        Assert.Equal(4, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Marketing", sql.Parameters[1].Value);
        Assert.Equal($"{sign}w3", sql.Parameters[3].ParameterName);
        Assert.Equal("Development", sql.Parameters[3].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ComplexWhereAND(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where(x => x.Where("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
            .And(x => x.Where("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
({escape}dept_no{escape} = {sign}w0 AND {escape}dept_name{escape} = {sign}w1) AND ({escape}dept_no{escape} = {sign}w2 AND {escape}dept_name{escape} = {sign}w3)", sql.Text);
        Assert.Equal(4, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Marketing", sql.Parameters[1].Value);
        Assert.Equal($"{sign}w3", sql.Parameters[3].ParameterName);
        Assert.Equal("Development", sql.Parameters[3].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNot(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").NotEqualTo("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} <> {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereGroupNot(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .WhereNot("dept_no").EqualTo("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
NOT ({escape}dept_no{escape} = {sign}w0)", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereGreaterThan(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").GreaterThan("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} > {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    private (string sign, string escape, string dbName) GetConstants(Database<EmployeesDb> employeesDb) =>
        (employeesDb.Provider.Constants.ParameterSign,
        employeesDb.Provider.Constants.EscapeCharacter,
        employeesDb.Provider.Constants.SupportsMultipleDatabases
            ? $"{employeesDb.Provider.Constants.EscapeCharacter}{employeesDb.Provider.DatabaseName}{employeesDb.Provider.Constants.EscapeCharacter}."
            : "");

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereGreaterThanOrEqual(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").GreaterThanOrEqual("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} >= {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereLessThan(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").LessThan("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} < {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereLessThanOrEqual(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").LessThanOrEqual("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} <= {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleLike(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").Like("d005%")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} LIKE {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005%", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderBy(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .OrderBy("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
ORDER BY {escape}dept_no{escape}", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByDesc(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .OrderByDesc("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
ORDER BY {escape}dept_no{escape} DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByTwice(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .OrderBy("dept_no")
            .OrderBy("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
ORDER BY {escape}dept_no{escape}, {escape}dept_name{escape}", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByDescTwice(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .OrderByDesc("dept_no")
            .OrderByDesc("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
ORDER BY {escape}dept_no{escape} DESC, {escape}dept_name{escape} DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByTwiceMixed(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .OrderBy("dept_no")
            .OrderByDesc("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
ORDER BY {escape}dept_no{escape}, {escape}dept_name{escape} DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByDescTwiceMixed(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .OrderByDesc("dept_no")
            .OrderBy("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
ORDER BY {escape}dept_no{escape} DESC, {escape}dept_name{escape}", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOrderBy(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .OrderBy("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} = {sign}w0
ORDER BY {escape}dept_no{escape}", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOrderByDesc(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .OrderByDesc("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} = {sign}w0
ORDER BY {escape}dept_no{escape} DESC", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Limit1(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Limit(1)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
LIMIT 1", sql.Text);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Limit2(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Limit(2)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
LIMIT 2", sql.Text);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Limit2Offset5(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Limit(2, 5)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
LIMIT 2 OFFSET 5", sql.Text);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOrderByDescLimit1(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .OrderByDesc("dept_no")
            .Limit(1)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} = {sign}w0
ORDER BY {escape}dept_no{escape} DESC
LIMIT 1", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereLimit1OrderByDesc(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .Limit(1)
            .OrderByDesc("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}dept_no{escape} = {sign}w0
ORDER BY {escape}dept_no{escape} DESC
LIMIT 1", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhat(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .What("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($"SELECT {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinExplicitAlias(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments", "d")
            .Join("dept_manager", "m").On(on => on.Where("dept_no", "d").EqualToColumn("dept_no", "m"))
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
JOIN {dbName}{escape}dept_manager{escape} m ON d.{escape}dept_no{escape} = m.{escape}dept_no{escape}", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAlias(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
JOIN {dbName}{escape}dept_manager{escape} m ON d.{escape}dept_no{escape} = m.{escape}dept_no{escape}", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAliasWhere(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .Where("m.dept_no").EqualTo("d005")
            .Limit(1)
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
JOIN {dbName}{escape}dept_manager{escape} m ON d.{escape}dept_no{escape} = m.{escape}dept_no{escape}
WHERE
m.{escape}dept_no{escape} = {sign}w0
ORDER BY d.{escape}dept_no{escape} DESC
LIMIT 1", sql.Text);
        Assert.NotEmpty(sql.Parameters);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAliasLimit(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .Limit(1)
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
JOIN {dbName}{escape}dept_manager{escape} m ON d.{escape}dept_no{escape} = m.{escape}dept_no{escape}
ORDER BY d.{escape}dept_no{escape} DESC
LIMIT 1", sql.Text);
        Assert.Empty(sql.Parameters);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAliasOrderByDesc(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
JOIN {dbName}{escape}dept_manager{escape} m ON d.{escape}dept_no{escape} = m.{escape}dept_no{escape}
ORDER BY d.{escape}dept_no{escape} DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void DoubleJoinIncludedAliasOrderByDesc(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .Join("dept-emp e").On(on => on.Where("e.dept_no").EqualToColumn("m.dept_no"))
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
JOIN {dbName}{escape}dept_manager{escape} m ON d.{escape}dept_no{escape} = m.{escape}dept_no{escape}
JOIN {dbName}{escape}dept-emp{escape} e ON e.{escape}dept_no{escape} = m.{escape}dept_no{escape}
ORDER BY d.{escape}dept_no{escape} DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleInsert(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var sql = employeesDb
            .From("departments")
            .Set("dept_no", "d005")
            .InsertQuery()
            .ToSql();

        Assert.Equal($@"INSERT INTO {dbName}{escape}departments{escape} ({escape}dept_no{escape}) VALUES ({sign}v0)", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}v0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleInsertWithLastId(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var lastInsert = employeesDb.Provider.Constants.LastInsertCommand;
        var sql = employeesDb
            .From("departments")
            .Set("dept_no", "d005")
            .AddLastIdQuery()
            .InsertQuery()
            .ToSql();

        Assert.Equal($@"INSERT INTO {dbName}{escape}departments{escape} ({escape}dept_no{escape}) VALUES ({sign}v0);
SELECT {lastInsert}", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}v0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereInOne(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var ids = new[] { 3 };
        var sql = employeesDb
            .From("departments d")
            .Where("Id").In(ids)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
WHERE
{escape}Id{escape} IN ({sign}w0)", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal(ids[0], sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereIn(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var ids = new[] { 1, 2, 3 };
        var sql = employeesDb
            .From("departments d")
            .Where("Id").In(ids)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT d.{escape}dept_no{escape}, d.{escape}dept_name{escape} FROM {dbName}{escape}departments{escape} d
WHERE
{escape}Id{escape} IN ({sign}w0, {sign}w1, {sign}w2)", sql.Text);
        Assert.Equal(3, sql.Parameters.Count);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal(ids[0], sql.Parameters[0].Value);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal(ids[1], sql.Parameters[1].Value);
        Assert.Equal($"{sign}w2", sql.Parameters[2].ParameterName);
        Assert.Equal(ids[2], sql.Parameters[2].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNotIn(Database<EmployeesDb> employeesDb)
    {
        var (sign, escape, dbName) = GetConstants(employeesDb);
        var ids = new[] { 1, 2, 3 };
        var sql = employeesDb
            .From("departments")
            .Where("Id").NotIn(ids)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT {escape}dept_no{escape}, {escape}dept_name{escape} FROM {dbName}{escape}departments{escape}
WHERE
{escape}Id{escape} NOT IN ({sign}w0, {sign}w1, {sign}w2)", sql.Text);
        Assert.Equal(3, sql.Parameters.Count);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal(ids[0], sql.Parameters[0].Value);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal(ids[1], sql.Parameters[1].Value);
        Assert.Equal($"{sign}w2", sql.Parameters[2].ParameterName);
        Assert.Equal(ids[2], sql.Parameters[2].Value);
    }

}