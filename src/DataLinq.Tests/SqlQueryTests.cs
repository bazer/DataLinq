using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests
{
    public class SqlQueryTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture fixture;

        public SqlQueryTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void SimpleWhere()
        {
            var departement = fixture.employeesDb
                .From<departments>()
                .Where("dept_no").EqualTo("d005")
                .Select();

            Assert.Single(departement);
            Assert.Equal("d005", departement.Single().dept_no);
        }

        //[Fact]
        //public void SimpleWhere2()
        //{
        //    var departement = fixture.employeesDb
        //        .From<departments>()
        //        .Where("dept_no").EqualTo("d005")
        //        .Select();

        //    var deleteResult = fixture.employeesDb
        //        .From<departments>()
        //        .Where("dept_no").EqualTo("d005")
        //        .Delete();

        //    var updateResult = fixture.employeesDb
        //        .From<departments>()
        //        .Where("dept_no").EqualTo("d005")
        //        .Set("dept_no", "d005")
        //        .Update();

        //    var insertResult = fixture.employeesDb
        //        .From<departments>()
        //        .Set("dept_no", "d005")
        //        .Insert();


        //    Assert.Single(departement);
        //    Assert.Equal("d005", departement.Single().dept_no);
        //}
    }
}