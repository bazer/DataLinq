using System;
using System.Linq;
using Slim.Metadata;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class QueryTests
    {
        private DatabaseFixture fixture;

        public QueryTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }


        [Fact]
        public void ToList()
        {
            Assert.Equal(9, fixture.Query.departments.ToList().Count);
        }

        [Fact]
        public void Count()
        {
            Assert.Equal(9, fixture.Query.departments.Count());
        }

        [Fact]
        public void SimpleWhere()
        {
            var where = fixture.Query.departments.Where(x => x.dept_no == "d005").ToList();
            Assert.Single(where);
            Assert.Equal("d005", where.First().dept_no);
        }

        [Fact]
        public void WhereAndToList()
        {
            var where = fixture.Query.dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateTime.Parse("1990-01-01")).ToList();
            Assert.Equal(2, where.Count);
        }

        [Fact]
        public void WhereAndCount()
        {
            var where = fixture.Query.dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateTime.Parse("1990-01-01"));
            Assert.Equal(2, where.Count());
        }

        [Fact]
        public void Single()
        {
            var dept = fixture.Query.departments.Single(x => x.dept_no == "d005");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.dept_no);
        }
    }
}