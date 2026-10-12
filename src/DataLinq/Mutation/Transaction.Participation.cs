using DataLinq.Interfaces;

namespace DataLinq.Mutation;

public partial class Transaction<T> where T : class, IDatabaseModel<T>
{
    /// <summary>Validates a new participating service operation without opening a provider transaction.</summary>
    public void ValidateParticipation() => EnsureCanRead("begin a participating service operation");
}
