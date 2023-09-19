using DataLinq.Tests.Models;
using System.Linq;
using Xunit;

namespace DataLinq.Tests
{
    public class SqlQueryTests : BaseTests
    {
        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhere(Database<Employees> employeesDb)
        {
            var departement = employeesDb
                .From<Department>()
                .Where("dept_no").EqualTo("d005")
                .Select();

            Assert.Single(departement);
            Assert.Equal("d005", departement.Single().DeptNo);
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