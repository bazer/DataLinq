using System;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;

namespace DataLinq;

public abstract partial class Database<T> where T : class, IDatabaseModel<T>
{
    // The provider owns the lifecycle. Multiple database wrappers and direct
    // provider disposal must not create independent cleanup attempts.
    internal ValueTask DisposeAsyncCore() => DisposeAsync();
}
