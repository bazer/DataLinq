using System;
using DataLinq.Exceptions;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Provides the shared SQL NULL validation required by column-aware
/// <see cref="IDataLinqDataReader"/> implementations.
/// </summary>
public static class DataLinqNullabilityContract
{
    internal static void EnsureDatabaseAllowsSqlNull(
        ColumnDefinition column,
        string sourceName)
    {
        Validate(column, sourceName);
        if (!column.Nullable)
        {
            throw CreateException(
                column,
                sourceName,
                DataLinqNullabilityMismatchKind.DatabaseColumn,
                column.ProviderClrType);
        }
    }

    internal static void EnsureModelAllowsSqlNull(
        ColumnDefinition column,
        string sourceName)
    {
        Validate(column, sourceName);
        EnsureDatabaseAllowsSqlNull(column, sourceName);
        if (!column.ValueProperty.CsNullable)
        {
            throw CreateException(
                column,
                sourceName,
                DataLinqNullabilityMismatchKind.ModelProperty,
                column.ModelClrType);
        }
    }

    /// <summary>
    /// Throws a <see cref="DataLinqNullabilityMismatchException"/> unless the generated database
    /// column, generated model property, and requested CLR type all permit a SQL NULL value.
    /// </summary>
    /// <param name="column">The generated column metadata associated with the reader ordinal.</param>
    /// <param name="requestedClrType">The CLR type requested by the generic reader call.</param>
    /// <param name="sourceName">A short, non-sensitive logical reader source label.</param>
    public static void EnsureReaderRequestAllowsSqlNull(
        ColumnDefinition column,
        Type requestedClrType,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(requestedClrType);
        EnsureModelAllowsSqlNull(column, sourceName);
        if (!CanRepresentNull(requestedClrType))
        {
            throw CreateException(
                column,
                sourceName,
                DataLinqNullabilityMismatchKind.RequestedClrType,
                requestedClrType);
        }
    }

    internal static DataLinqNullabilityMismatchException CreateModelMismatch(
        ColumnDefinition column,
        string sourceName)
    {
        Validate(column, sourceName);
        return CreateException(
            column,
            sourceName,
            DataLinqNullabilityMismatchKind.ModelProperty,
            column.ModelClrType);
    }

    private static bool CanRepresentNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static void Validate(ColumnDefinition column, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(column);
        ProviderRowMaterializer.ValidateSourceName(sourceName);
    }

    private static DataLinqNullabilityMismatchException CreateException(
        ColumnDefinition column,
        string sourceName,
        DataLinqNullabilityMismatchKind mismatchKind,
        Type? expectedClrType) =>
        new(
            column.Table.DbName,
            column.DbName,
            column.ValueProperty.PropertyName,
            column.ValueProperty.Model.CsType.Name,
            sourceName,
            mismatchKind,
            expectedClrType);
}
