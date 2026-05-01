using System.Threading.Tasks;
using DataLinq.Config;

namespace DataLinq.Tests.Unit.Core;

public class DataLinqConnectionStringTests
{
    [Test]
    public async Task ChangeValue_ReplacesCaseInsensitiveExistingKey()
    {
        var connectionString = new DataLinqConnectionString("Data Source=relative.db;Cache=Shared");

        var changed = connectionString.ChangeValue("Data Source", "absolute.db");

        await Assert.That(changed.GetValue("Data Source")).IsEqualTo("absolute.db");
        await Assert.That(changed.Original).DoesNotContain("relative.db");
    }
}
