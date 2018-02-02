using System;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class Core
    {
        private DatabaseFixture fixture;

        public Core(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void Test1()
        {

        }
    }
}
