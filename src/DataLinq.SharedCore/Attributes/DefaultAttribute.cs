using System;

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
    public object Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class DefaultAttribute<T>(T value) : DefaultAttribute(value ?? throw new ArgumentNullException(nameof(value)))
{
    public new T Value => (T)base.Value;
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