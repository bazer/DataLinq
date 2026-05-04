using System.Diagnostics.CodeAnalysis;

namespace DataLinq.Interfaces;

public interface IDataSourceAccess
{
    IDatabaseProvider Provider { get; }
    IDatabaseAccess DatabaseAccess { get; }
}

public interface IDataSourceAccess<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicProperties)]
    T> : IDataSourceAccess
    where T : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<T>
{
    new IDatabaseProvider<T> Provider { get; }
    public T Query();
}
