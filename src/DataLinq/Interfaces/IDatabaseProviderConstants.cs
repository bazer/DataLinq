namespace DataLinq.Interfaces;

public interface IDatabaseProviderConstants
{
    string ParameterSign { get; }
    string LastInsertCommand { get; }
    string EscapeCharacter { get; }
    bool SupportsMultipleDatabases { get; }

    /// <summary>
    /// Gets the provider-specific clause used after an INSERT target when every
    /// column is intentionally omitted so the server can apply its defaults,
    /// or <see langword="null"/> when that shape is not supported.
    /// </summary>
    string? DefaultValuesInsertClause => null;
}
