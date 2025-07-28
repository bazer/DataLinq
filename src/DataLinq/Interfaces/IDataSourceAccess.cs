namespace DataLinq.Interfaces;

public interface IDataSourceAccess
{
    IDatabaseProvider Provider { get; }
    IDatabaseAccess DatabaseAccess { get; }
}

public interface IDataSourceAccess<T> : IDataSourceAccess
    where T : class, IDatabaseModel
{
    new IDatabaseProvider<T> Provider { get; }
    public T Query();
}