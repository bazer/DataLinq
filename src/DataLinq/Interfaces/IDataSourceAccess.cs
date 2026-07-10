using DataLinq.Metadata;

namespace DataLinq.Interfaces;

public interface IDataSourceAccess : IDataLinqReadSource
{
    IDatabaseProvider Provider { get; }
    IDatabaseAccess DatabaseAccess { get; }

    DatabaseDefinition IDataLinqReadSource.Metadata => Provider.Metadata;
}

public interface IDataSourceAccess<T> : IDataSourceAccess
    where T : class, IDatabaseModel<T>
{
    new IDatabaseProvider<T> Provider { get; }
    public T Query();
}
