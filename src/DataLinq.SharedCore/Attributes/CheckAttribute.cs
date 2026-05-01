using System;
using DataLinq.Metadata;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class CheckAttribute : Attribute
{
    public CheckAttribute(string name, string expression)
    {
        DatabaseType = DatabaseType.Default;
        Name = name;
        Expression = expression;
    }

    public CheckAttribute(DatabaseType databaseType, string name, string expression)
    {
        DatabaseType = databaseType;
        Name = name;
        Expression = expression;
    }

    public DatabaseType DatabaseType { get; }
    public string Name { get; }
    public string Expression { get; }
}
