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
    public class SqlQueryTests
    {
        private readonly DatabaseFixture fixture;

        public SqlQueryTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void SimpleWhere()
        {
            var departement = fixture.employeesDb.Transaction()
                .Query<departments>()
                .Select()
                .Where("dept_no").EqualTo("d005")
                .Query()
                .Execute<departments>();

            Assert.Single(departement);
            Assert.Equal("d005", departement.Single().dept_no);
        }
    }
}