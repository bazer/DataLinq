using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.MySql;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class ThreadingTests
    {
        private readonly DatabaseFixture fixture;

        public ThreadingTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void ReadParallel()
        {
            Parallel.For(0, 10, i =>
            {
                SetAndTest(10004);
                SetAndTest(10005);
                SetAndTest(10006);
                SetAndTest(10007);
                SetAndTest(10008);
            });
        }

        private void SetAndTest(int value)
        {
            var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == value);
            Assert.Equal(value, employee.emp_no);
        }
    }
}