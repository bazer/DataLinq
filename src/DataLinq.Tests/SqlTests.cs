using DataLinq.Query;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests;

public class SqlTests : BaseTests
{
    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhere(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .SelectQuery()
            .ToSql();


        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereAnd(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where("dept_no").EqualTo("d005")
            .And("dept_name").EqualTo("Development")
            .And("dept_name").EqualTo("Development")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = {sign}w0 AND dept_name = {sign}w1 AND dept_name = {sign}w2", sql.Text);
        Assert.Equal(3, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Development", sql.Parameters[1].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOr(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where("dept_no").EqualTo("d005")
            .Or("dept_name").EqualTo("Development")
            .Or("dept_name").EqualTo("Development")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = {sign}w0 OR dept_name = {sign}w1 OR dept_name = {sign}w2", sql.Text);
        Assert.Equal(3, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Development", sql.Parameters[1].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ComplexWhereOR(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where(x => x("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
            .Or(x => x("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
(dept_no = {sign}w0 AND dept_name = {sign}w1) OR (dept_no = {sign}w2 AND dept_name = {sign}w3)", sql.Text);
        Assert.Equal(4, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Marketing", sql.Parameters[1].Value);
        Assert.Equal($"{sign}w3", sql.Parameters[3].ParameterName);
        Assert.Equal("Development", sql.Parameters[3].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ComplexWhereAND(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = new SqlQuery("departments", employeesDb.Transaction())
            .Where(x => x("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
            .And(x => x("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
(dept_no = {sign}w0 AND dept_name = {sign}w1) AND (dept_no = {sign}w2 AND dept_name = {sign}w3)", sql.Text);
        Assert.Equal(4, sql.Parameters.Count);
        Assert.Equal($"{sign}w1", sql.Parameters[1].ParameterName);
        Assert.Equal("Marketing", sql.Parameters[1].Value);
        Assert.Equal($"{sign}w3", sql.Parameters[3].ParameterName);
        Assert.Equal("Development", sql.Parameters[3].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNot(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").NotEqualTo("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no <> {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereGroupNot(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .WhereNot("dept_no").EqualTo("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
NOT (dept_no = {sign}w0)", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereGreaterThan(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").GreaterThan("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no > {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereGreaterThanOrEqual(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").GreaterThanOrEqual("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no >= {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereLessThan(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").LessThan("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no < {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereLessThanOrEqual(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").LessThanOrEqual("d005")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no <= {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleLike(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").Like("d005%")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no LIKE {sign}w0", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005%", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderBy(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .OrderBy("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByDesc(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .OrderByDesc("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByTwice(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .OrderBy("dept_no")
            .OrderBy("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no, dept_name", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByDescTwice(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .OrderByDesc("dept_no")
            .OrderByDesc("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no DESC, dept_name DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByTwiceMixed(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .OrderBy("dept_no")
            .OrderByDesc("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no, dept_name DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleOrderByDescTwiceMixed(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .OrderByDesc("dept_no")
            .OrderBy("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no DESC, dept_name", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOrderBy(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .OrderBy("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = {sign}w0
ORDER BY dept_no", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOrderByDesc(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .OrderByDesc("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = {sign}w0
ORDER BY dept_no DESC", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Limit1(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Limit(1)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
LIMIT 1", sql.Text);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Limit2(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Limit(2)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
LIMIT 2", sql.Text);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Limit2Offset5(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Limit(2, 5)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
LIMIT 2 OFFSET 5", sql.Text);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereOrderByDescLimit1(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .OrderByDesc("dept_no")
            .Limit(1)
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = {sign}w0
ORDER BY dept_no DESC
LIMIT 1", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereLimit1OrderByDesc(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Where("dept_no").EqualTo("d005")
            .Limit(1)
            .OrderByDesc("dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = {sign}w0
ORDER BY dept_no DESC
LIMIT 1", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}w0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhat(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .What("dept_name")
            .SelectQuery()
            .ToSql();

        Assert.Equal("SELECT dept_name FROM departments", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinExplicitAlias(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments", "d")
            .Join("dept_manager", "m").On("dept_no", "d").EqualToColumn("dept_no", "m")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAlias(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On("d.dept_no").EqualToColumn("m.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAliasWhere(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On("d.dept_no").EqualToColumn("m.dept_no")
            .Where("m.dept_no").EqualTo("d005")
            .Limit(1)
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no
WHERE
m.dept_no = {sign}w0
ORDER BY d.dept_no DESC
LIMIT 1", sql.Text);
        Assert.NotEmpty(sql.Parameters);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAliasLimit(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On("d.dept_no").EqualToColumn("m.dept_no")
            .Limit(1)
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no
ORDER BY d.dept_no DESC
LIMIT 1", sql.Text);
        Assert.Empty(sql.Parameters);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleJoinIncludedAliasOrderByDesc(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On("d.dept_no").EqualToColumn("m.dept_no")
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no
ORDER BY d.dept_no DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void DoubleJoinIncludedAliasOrderByDesc(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments d")
            .Join("dept_manager m").On("d.dept_no").EqualToColumn("m.dept_no")
            .Join("dept_emp e").On("e.dept_no").EqualToColumn("m.dept_no")
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        Assert.Equal($@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no
JOIN dept_emp e ON e.dept_no = m.dept_no
ORDER BY d.dept_no DESC", sql.Text);
        Assert.Empty(sql.Parameters);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleInsert(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var sql = employeesDb
            .From("departments")
            .Set("dept_no", "d005")
            .InsertQuery()
            .ToSql();

        Assert.Equal($@"INSERT INTO departments (dept_no) VALUES ({sign}v0)", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}v0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleInsertWithLastId(Database<Employees> employeesDb)
    {
        var sign = employeesDb.Provider.Constants.ParameterSign;
        var lastInsert = employeesDb.Provider.Constants.LastInsertCommand;
        var sql = employeesDb
            .From("departments")
            .Set("dept_no", "d005")
            .AddLastIdQuery()
            .InsertQuery()
            .ToSql();

        Assert.Equal($@"INSERT INTO departments (dept_no) VALUES ({sign}v0);
SELECT {lastInsert}", sql.Text);
        Assert.Single(sql.Parameters);
        Assert.Equal($"{sign}v0", sql.Parameters[0].ParameterName);
        Assert.Equal("d005", sql.Parameters[0].Value);
    }
}