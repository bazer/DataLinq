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

/// <summary>
/// Declares a fixed model <see cref="Guid"/> default using a legal,
/// storage-neutral C# attribute argument.
/// </summary>
/// <remarks>
/// The string is a source carrier only. DataLinq metadata represents the
/// default as the parsed <see cref="Guid"/> value.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class DefaultGuidAttribute : DefaultAttribute
{
    /// <summary>
    /// Creates a fixed model <see cref="Guid"/> default from its exact
    /// 36-character <c>D</c> representation.
    /// </summary>
    /// <param name="value">
    /// The fixed Guid in <c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx</c> form.
    /// </param>
    public DefaultGuidAttribute(string value) : base(ParseExactD(value))
    {
    }

    internal static bool TryParseExactD(string? value, out Guid guid)
    {
        guid = default;

        if (value is null || value.Length != 36)
            return false;

        for (var index = 0; index < value.Length; index++)
        {
            if (index is 8 or 13 or 18 or 23)
            {
                if (value[index] != '-')
                    return false;

                continue;
            }

            if (!IsHexDigit(value[index]))
                return false;
        }

        return Guid.TryParseExact(value, "D", out guid);
    }

    private static Guid ParseExactD(string value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        if (!TryParseExactD(value, out var guid))
        {
            throw new ArgumentException(
                "Default Guid values must use the exact 36-character 'D' format " +
                "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx. Compact, braced, and parenthesized forms are not accepted.",
                nameof(value));
        }

        return guid;
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
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
