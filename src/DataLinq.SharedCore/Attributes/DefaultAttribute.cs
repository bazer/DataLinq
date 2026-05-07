using System;
using DataLinq.Metadata;

namespace DataLinq.Attributes;

public enum DynamicFunctions
{
    /// <summary>
    /// Use the current date and time.
    /// MySQL/MariaDB: maps to CURRENT_TIMESTAMP.
    /// SQLite: maps to CURRENT_TIMESTAMP.
    /// </summary>
    CurrentTimestamp,
    NewUUID
}

public enum UUIDVersion
{
    Version4,
    Version7
}


[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class DefaultAttribute(object value) : Attribute
{
    public DefaultAttribute(object value, string? codeExpression) : this(value)
    {
        SetCodeExpressionCore(codeExpression);
    }

    public object Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
    public string? CodeExpression { get; private set; }
    public bool IsFrozen { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public DefaultAttribute SetCodeExpression(string? codeExpression)
    {
        return SetCodeExpressionCore(codeExpression);
    }

    internal DefaultAttribute SetCodeExpressionCore(string? codeExpression)
    {
        MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
        CodeExpression = codeExpression;
        return this;
    }

    internal void Freeze()
    {
        IsFrozen = true;
    }
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class DefaultAttribute<T>(T value) : DefaultAttribute(value ?? throw new ArgumentNullException(nameof(value)))
{
    public DefaultAttribute(T value, string? codeExpression) : this(value)
    {
        SetCodeExpressionCore(codeExpression);
    }

    public new T Value => (T)base.Value;
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class DefaultSqlAttribute(DatabaseType databaseType, string expression) : DefaultAttribute(expression)
{
    public DatabaseType DatabaseType { get; } = databaseType;
    public string Expression { get; } = string.IsNullOrWhiteSpace(expression)
        ? throw new ArgumentException("Default SQL expression cannot be empty.", nameof(expression))
        : expression;
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class DefaultCurrentTimestampAttribute() : DefaultAttribute(DynamicFunctions.CurrentTimestamp)
{
    public DynamicFunctions DateTimeDefault => (DynamicFunctions)Value;
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class DefaultNewUUIDAttribute(UUIDVersion version = UUIDVersion.Version7) : DefaultAttribute(DynamicFunctions.NewUUID)
{
    public DynamicFunctions NewUUID => (DynamicFunctions)Value;
    public UUIDVersion Version { get; } = version;
}
