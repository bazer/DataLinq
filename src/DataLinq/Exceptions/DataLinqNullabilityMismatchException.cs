using System;

namespace DataLinq.Exceptions;

/// <summary>
/// Identifies the nullability contract that rejected a SQL <see langword="NULL"/> value.
/// </summary>
public enum DataLinqNullabilityMismatchKind
{
    /// <summary>The generated database-column metadata declares the column non-nullable.</summary>
    DatabaseColumn,

    /// <summary>The generated model property is declared non-nullable.</summary>
    ModelProperty,

    /// <summary>The CLR type requested from the data reader cannot represent <see langword="null"/>.</summary>
    RequestedClrType
}

/// <summary>
/// The exception thrown when SQL returns <see langword="NULL"/> for a DataLinq read contract that
/// does not permit a null value.
/// </summary>
public sealed class DataLinqNullabilityMismatchException : InvalidOperationException
{
    internal DataLinqNullabilityMismatchException(
        string tableName,
        string columnName,
        string propertyName,
        string modelName,
        string sourceName,
        DataLinqNullabilityMismatchKind mismatchKind,
        Type? expectedClrType)
        : base(BuildMessage(
            tableName,
            columnName,
            propertyName,
            modelName,
            sourceName,
            mismatchKind,
            expectedClrType))
    {
        TableName = tableName;
        ColumnName = columnName;
        PropertyName = propertyName;
        ModelName = modelName;
        SourceName = sourceName;
        MismatchKind = mismatchKind;
        ExpectedClrType = expectedClrType;
    }

    /// <summary>Gets the database table name from the generated DataLinq metadata.</summary>
    public string TableName { get; }

    /// <summary>Gets the database column name from the generated DataLinq metadata.</summary>
    public string ColumnName { get; }

    /// <summary>Gets the generated model property name mapped to the column.</summary>
    public string PropertyName { get; }

    /// <summary>Gets the generated model type name that owns the property.</summary>
    public string ModelName { get; }

    /// <summary>Gets the short, non-sensitive logical read-path label.</summary>
    public string SourceName { get; }

    /// <summary>Gets the nullability contract that rejected the SQL <see langword="NULL"/> value.</summary>
    public DataLinqNullabilityMismatchKind MismatchKind { get; }

    /// <summary>
    /// Gets the model or requested CLR type associated with the mismatch when runtime type metadata
    /// is available.
    /// </summary>
    public Type? ExpectedClrType { get; }

    private static string BuildMessage(
        string tableName,
        string columnName,
        string propertyName,
        string modelName,
        string sourceName,
        DataLinqNullabilityMismatchKind mismatchKind,
        Type? expectedClrType)
    {
        var location = $"column '{tableName}.{columnName}' mapped to model property '{modelName}.{propertyName}'";
        var contract = mismatchKind switch
        {
            DataLinqNullabilityMismatchKind.DatabaseColumn =>
                "the generated database-column metadata declares the column non-nullable",
            DataLinqNullabilityMismatchKind.ModelProperty =>
                "the generated model property is non-nullable",
            DataLinqNullabilityMismatchKind.RequestedClrType =>
                $"requested CLR type '{expectedClrType?.FullName ?? "<unresolved>"}' cannot represent null",
            _ => throw new ArgumentOutOfRangeException(nameof(mismatchKind), mismatchKind, null)
        };
        var remediation = mismatchKind == DataLinqNullabilityMismatchKind.RequestedClrType
            ? "Request a nullable CLR type or another CLR type capable of representing SQL NULL."
            : "Check for schema drift or mark the model property nullable and regenerate the DataLinq model.";

        return
            $"SQL returned NULL for {location} from source '{sourceName}', but {contract}. " +
            remediation;
    }
}
