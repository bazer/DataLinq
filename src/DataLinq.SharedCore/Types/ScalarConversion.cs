using System;
using DataLinq.Metadata;

namespace DataLinq;

public readonly record struct ScalarConversionContext(
    ColumnDefinition Column,
    DatabaseType DatabaseType);

public interface IDataLinqScalarConverter
{
    Type ModelType { get; }
    Type ProviderType { get; }

    object? ToProviderObject(object? modelValue, in ScalarConversionContext context);
    object? FromProviderObject(object? providerValue, in ScalarConversionContext context);
}

public abstract class DataLinqScalarConverter<TModel, TProvider> : IDataLinqScalarConverter
{
    public Type ModelType => typeof(TModel);
    public Type ProviderType => typeof(TProvider);

    public abstract TProvider ToProvider(TModel modelValue, in ScalarConversionContext context);
    public abstract TModel FromProvider(TProvider providerValue, in ScalarConversionContext context);

    public object? ToProviderObject(object? modelValue, in ScalarConversionContext context)
    {
        if (modelValue is null)
            return null;

        return ToProvider((TModel)modelValue, context);
    }

    public object? FromProviderObject(object? providerValue, in ScalarConversionContext context)
    {
        if (providerValue is null)
            return null;

        return FromProvider((TProvider)providerValue, context);
    }
}
