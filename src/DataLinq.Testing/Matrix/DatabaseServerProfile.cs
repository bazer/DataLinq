using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Testing;

public sealed record DatabaseServerProfile(
    string Id,
    string DisplayName,
    bool IsDefault,
    IReadOnlyList<DatabaseServerTarget> Targets)
{
    public DatabaseServerTarget? MySqlTarget => Targets.SingleOrDefault(x => x.Family == DatabaseServerFamily.MySql);
    public DatabaseServerTarget? MariaDbTarget => Targets.SingleOrDefault(x => x.Family == DatabaseServerFamily.MariaDb);

    public static DatabaseServerProfile Create(
        string id,
        string displayName,
        bool isDefault,
        IEnumerable<DatabaseServerTarget> targets)
    {
        return new DatabaseServerProfile(
            id,
            displayName,
            isDefault,
            new ReadOnlyCollection<DatabaseServerTarget>(targets.ToArray()));
    }
}
