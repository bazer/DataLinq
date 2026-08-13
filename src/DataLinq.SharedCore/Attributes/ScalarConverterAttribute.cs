using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ScalarConverterAttribute : Attribute
{
    public ScalarConverterAttribute(Type converterType)
    {
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }

    public Type ConverterType { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ScalarConverterRegistrationAttribute : Attribute
{
    public ScalarConverterRegistrationAttribute(Type modelType, Type converterType)
    {
        ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }

    public Type ModelType { get; }
    public Type ConverterType { get; }
}
