using System;
using DataLinq.Metadata;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class CommentAttribute : Attribute
{
    public CommentAttribute(string text)
    {
        DatabaseType = DatabaseType.Default;
        Text = text;
    }

    public CommentAttribute(DatabaseType databaseType, string text)
    {
        DatabaseType = databaseType;
        Text = text;
    }

    public DatabaseType DatabaseType { get; }
    public string Text { get; }
}
