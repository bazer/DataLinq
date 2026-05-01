using DataLinq.Interfaces;

namespace DataLinq.Validation;

public sealed class SchemaDifference
{
    public SchemaDifference(
        SchemaDifferenceKind kind,
        SchemaDifferenceSeverity severity,
        SchemaDifferenceSafety safety,
        string path,
        string message,
        IDefinition? modelDefinition = null,
        IDefinition? databaseDefinition = null)
    {
        Kind = kind;
        Severity = severity;
        Safety = safety;
        Path = path;
        Message = message;
        ModelDefinition = modelDefinition;
        DatabaseDefinition = databaseDefinition;
    }

    public SchemaDifferenceKind Kind { get; }
    public SchemaDifferenceSeverity Severity { get; }
    public SchemaDifferenceSafety Safety { get; }
    public string Path { get; }
    public string Message { get; }
    public IDefinition? ModelDefinition { get; }
    public IDefinition? DatabaseDefinition { get; }

    public override string ToString() => $"{Severity} {Kind} {Path}: {Message}";
}
