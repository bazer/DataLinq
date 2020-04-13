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

//        [Fact]
//        public void ComplexWhereOR()
//        {
//            var sql = new Query("departments", transaction)
//                .Where(x => x("dept_no").EqualTo("d001").And("dept_name").EqualTo("Marketing"))
//                .Or(x => x("dept_no").EqualTo("d005").And("dept_name").EqualTo("Development"))
//                .SelectQuery()
//                .ToSql();

//            Assert.Equal(@"SELECT dept_no, dept_name FROM departments 
//WHERE
//(dept_no = ?w0 AND dept_name = ?w1) OR
//(dept_no = ?w2 AND dept_name = ?w3)", sql.Text);
//            Assert.Equal(4, sql.Parameters.Count);
//            Assert.Equal("?w1", sql.Parameters[1].ParameterName);
//            Assert.Equal("Development", sql.Parameters[1].Value);
//        }

        //[Fact]
        //public void SimpleWhereReverse()
        //{
        //    var where = fixture.employeesDb.departments.Where(x => "d005" == x.dept_no).ToList();
        //    Assert.Single(where);
        //    Assert.Equal("d005", where[0].dept_no);
        //}

        //[Fact]
        //public void SimpleWhereNot()
        //{
        //    var where = fixture.employeesDb.departments.Where(x => x.dept_no != "d005").ToList();
        //    Assert.Equal(8, where.Count);
        //    Assert.DoesNotContain(where, x => x.dept_no == "d005");
        //}

        //[Fact]
        //public void WhereAndToList()
        //{
        //    var where = fixture.employeesDb.dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateTime.Parse("1990-01-01")).ToList();
        //    Assert.Equal(2, where.Count);
        //}

        //[Fact]
        //public void WhereAndCount()
        //{
        //    var where = fixture.employeesDb.dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateTime.Parse("1990-01-01"));
        //    Assert.Equal(2, where.Count());
        //}

        //[Fact]
        //public void Single()
        //{
        //    var dept = fixture.employeesDb.departments.Single(x => x.dept_no == "d005");
        //    Assert.NotNull(dept);
        //    Assert.Equal("d005", dept.dept_no);
        //}

        //[Fact]
        //public void Any()
        //{
        //    Assert.True(fixture.employeesDb.departments.Any(x => x.dept_no == "d005"));
        //    Assert.True(fixture.employeesDb.departments.Where(x => x.dept_no == "d005").Any());
        //    Assert.False(fixture.employeesDb.departments.Any(x => x.dept_no == "not_existing"));
        //    Assert.False(fixture.employeesDb.departments.Where(x => x.dept_no == "not_existing").Any());
        //}

        //[Fact]
        //public void OrderBy()
        //{
        //    var deptByDeptNo = fixture.employeesDb.departments.OrderBy(x => x.dept_no);
        //    Assert.Equal("d001", deptByDeptNo.FirstOrDefault().dept_no);
        //    Assert.Equal("d009", deptByDeptNo.Last().dept_no);


        //}
    }
}