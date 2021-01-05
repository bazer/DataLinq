using System;
using System.Linq;
using Slim.Metadata;
using Slim.Mutation;
using Slim.Query;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class SqlTests
    {
        private readonly DatabaseFixture fixture;
        private readonly Transaction<employeesDb> transaction;

        public SqlTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
            this.transaction = fixture.employeesDb.Transaction(Slim.Mutation.TransactionType.ReadOnly);
        }

        [Fact]
        public void SimpleWhere()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").EqualTo("d005")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = ?w0", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhereAnd()
        {
            var sql = new SqlQuery("departments", transaction)
                .Where("dept_no").EqualTo("d005")
                .And("dept_name").EqualTo("Development")
                .And("dept_name").EqualTo("Development")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = ?w0 AND dept_name = ?w1 AND dept_name = ?w2", sql.Text);
            Assert.Equal(3, sql.Parameters.Count);
            Assert.Equal("?w1", sql.Parameters[1].ParameterName);
            Assert.Equal("Development", sql.Parameters[1].Value);
        }

        [Fact]
        public void SimpleWhereOr()
        {
            var sql = new SqlQuery("departments", transaction)
                .Where("dept_no").EqualTo("d005")
                .Or("dept_name").EqualTo("Development")
                .Or("dept_name").EqualTo("Development")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = ?w0 OR dept_name = ?w1 OR dept_name = ?w2", sql.Text);
            Assert.Equal(3, sql.Parameters.Count);
            Assert.Equal("?w1", sql.Parameters[1].ParameterName);
            Assert.Equal("Development", sql.Parameters[1].Value);
        }

        [Fact]
        public void ComplexWhereOR()
        {
            var sql = new SqlQuery("departments", transaction)
                .Where(x => x("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
                .Or(x => x("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
(dept_no = ?w0 AND dept_name = ?w1) OR (dept_no = ?w2 AND dept_name = ?w3)", sql.Text);
            Assert.Equal(4, sql.Parameters.Count);
            Assert.Equal("?w1", sql.Parameters[1].ParameterName);
            Assert.Equal("Marketing", sql.Parameters[1].Value);
            Assert.Equal("?w3", sql.Parameters[3].ParameterName);
            Assert.Equal("Development", sql.Parameters[3].Value);
        }

        [Fact]
        public void ComplexWhereAND()
        {
            var sql = new SqlQuery("departments", transaction)
                .Where(x => x("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
                .And(x => x("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
(dept_no = ?w0 AND dept_name = ?w1) AND (dept_no = ?w2 AND dept_name = ?w3)", sql.Text);
            Assert.Equal(4, sql.Parameters.Count);
            Assert.Equal("?w1", sql.Parameters[1].ParameterName);
            Assert.Equal("Marketing", sql.Parameters[1].Value);
            Assert.Equal("?w3", sql.Parameters[3].ParameterName);
            Assert.Equal("Development", sql.Parameters[3].Value);
        }

        [Fact]
        public void SimpleWhereNot()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").NotEqualTo("d005")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no <> ?w0", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhereGreaterThan()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").GreaterThan("d005")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no > ?w0", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhereGreaterThanOrEqual()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").GreaterThanOrEqual("d005")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no >= ?w0", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhereLessThan()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").LessThan("d005")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no < ?w0", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhereLessThanOrEqual()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").LessThanOrEqual("d005")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no <= ?w0", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleOrderBy()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .OrderBy("dept_no")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no", sql.Text);
            Assert.Empty(sql.Parameters);
        }

        [Fact]
        public void SimpleOrderByDesc()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .OrderByDesc("dept_no")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no DESC", sql.Text);
            Assert.Empty(sql.Parameters);
        }


        [Fact]
        public void SimpleOrderByTwice()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .OrderBy("dept_no")
                .OrderBy("dept_name")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no, dept_name", sql.Text);
            Assert.Empty(sql.Parameters);
        }

        [Fact]
        public void SimpleOrderByDescTwice()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .OrderByDesc("dept_no")
                .OrderByDesc("dept_name")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no DESC, dept_name DESC", sql.Text);
            Assert.Empty(sql.Parameters);
        }

        [Fact]
        public void SimpleOrderByTwiceMixed()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .OrderBy("dept_no")
                .OrderByDesc("dept_name")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no, dept_name DESC", sql.Text);
            Assert.Empty(sql.Parameters);
        }

        [Fact]
        public void SimpleOrderByDescTwiceMixed()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .OrderByDesc("dept_no")
                .OrderBy("dept_name")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
ORDER BY dept_no DESC, dept_name", sql.Text);
            Assert.Empty(sql.Parameters);
        }

        [Fact]
        public void SimpleWhereOrderBy()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").EqualTo("d005")
                .OrderBy("dept_no")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = ?w0
ORDER BY dept_no", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhereOrderByDesc()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").EqualTo("d005")
                .OrderByDesc("dept_no")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = ?w0
ORDER BY dept_no DESC", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void Limit1()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Limit(1)
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
LIMIT 0, 1", sql.Text);
        }

        [Fact]
        public void Limit2()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Limit(2)
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
LIMIT 0, 2", sql.Text);
        }

        [Fact]
        public void Limit2Offset5()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Limit(5, 2)
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
LIMIT 5, 2", sql.Text);
        }

        [Fact]
        public void SimpleWhereOrderByDescLimit1()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").EqualTo("d005")
                .OrderByDesc("dept_no")
                .Limit(1)
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = ?w0
ORDER BY dept_no DESC
LIMIT 0, 1", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhereLimit1OrderByDesc()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .Where("dept_no").EqualTo("d005")
                .Limit(1)
                .OrderByDesc("dept_no")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments
WHERE
dept_no = ?w0
ORDER BY dept_no DESC
LIMIT 0, 1", sql.Text);
            Assert.Single(sql.Parameters);
            Assert.Equal("?w0", sql.Parameters[0].ParameterName);
            Assert.Equal("d005", sql.Parameters[0].Value);
        }

        [Fact]
        public void SimpleWhat()
        {
            var sql = fixture.employeesDb
                .From("departments")
                .What("dept_name")
                .SelectQuery()
                .ToSql();

            Assert.Equal("SELECT dept_name FROM departments", sql.Text);
            Assert.Empty(sql.Parameters);
        }

        [Fact]
        public void SimpleJoinExplicitAlias()
        {
            var sql = fixture.employeesDb
                .From("departments", "d")
                .Join("dept_manager", "m").On("dept_no", "d").EqualToColumn("dept_no", "m")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no", sql.Text);
            Assert.Empty(sql.Parameters);
        }

        [Fact]
        public void SimpleJoinIncludedAlias()
        {
            var sql = fixture.employeesDb
                .From("departments d")
                .Join("dept_manager m").On("d.dept_no").EqualToColumn("m.dept_no")
                .SelectQuery()
                .ToSql();

            Assert.Equal(@"SELECT dept_no, dept_name FROM departments d
JOIN dept_manager m ON d.dept_no = m.dept_no", sql.Text);
            Assert.Empty(sql.Parameters);
        }
    }
}