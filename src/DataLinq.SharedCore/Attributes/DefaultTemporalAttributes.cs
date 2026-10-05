using System;
using System.Globalization;

namespace DataLinq.Attributes;

/// <summary>Declares a fixed DateTime default without a runtime expression in the attribute argument.</summary>
/// <remarks>The text has no timezone suffix; Kind is explicit and no timezone conversion is performed.</remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class DefaultDateTimeAttribute : DefaultAttribute
{
    public DefaultDateTimeAttribute(string value, DateTimeKind kind = DateTimeKind.Unspecified)
        : base(DateTime.SpecifyKind(DateTime.ParseExact(value,
            ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
            CultureInfo.InvariantCulture, DateTimeStyles.None), kind)) { }
}

/// <summary>Declares a fixed DateTimeOffset default, preserving its explicit offset and fractional seconds.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class DefaultDateTimeOffsetAttribute : DefaultAttribute
{
    public DefaultDateTimeOffsetAttribute(string value)
        : base(DateTimeOffset.ParseExact(value,
            ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.None)) { }
}

/// <summary>Declares a fixed TimeSpan default using the invariant constant (c) format.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class DefaultTimeSpanAttribute : DefaultAttribute
{
    public DefaultTimeSpanAttribute(string value)
        : base(TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture)) { }
}
