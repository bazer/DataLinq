using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Mutation
{
    public class History
    {
        public List<StateChange> Changes { get; }

        public void AddChanges(params StateChange[] changes)
        {
            Changes.AddRange(changes);
        }
    }
}
