using Microsoft.CodeAnalysis;

namespace DataLinq.SourceGenerators;

internal static class GeneratorDiagnostics
{
    internal static readonly DiagnosticDescriptor AsyncNavigationConflict = new(
        "DLG004",
        "Async navigation member conflict",
        "Relation '{0}' cannot generate '{1}': member '{2}' conflicts because {3}",
        "DataLinq.Generators",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor MetadataGenerationFailed = new(
        "DLG001",
        "Database Metadata Generation Failed",
        "{0}",
        "DataLinq.Generators",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor ModelFileGenerationFailed = new(
        "DLG002",
        "Model File Generation Failed",
        "{0}",
        "DataLinq.Generators",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor InvalidDefaultValue = new(
        "DLG003",
        "Invalid model default value",
        "Default expression '{0}' is not assignable to property '{1}' of type '{2}'",
        "DataLinq.Generators",
        DiagnosticSeverity.Error,
        true);
}
