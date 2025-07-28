namespace DataLinq.Interfaces;

public interface IDataSourceAccess
{
    IDatabaseProvider Provider { get; }
    IDatabaseAccess DatabaseAccess { get; }
}