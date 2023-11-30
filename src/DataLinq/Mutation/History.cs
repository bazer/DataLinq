using System.Collections.Generic;

namespace DataLinq.Mutation;

public class History
{
    public List<StateChange> Changes { get; }

    public void AddChanges(params StateChange[] changes)
    {
        Changes.AddRange(changes);
    }
}
