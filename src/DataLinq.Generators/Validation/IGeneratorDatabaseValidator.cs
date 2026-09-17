using DataLinq.Metadata;
using Microsoft.CodeAnalysis;

namespace DataLinq.SourceGenerators;

internal interface IGeneratorDatabaseValidator
{
    void Validate(DatabaseDefinition database, Compilation compilation, System.Threading.CancellationToken cancellationToken,
        System.Action<Diagnostic> reportDiagnostic, GeneratorValidationContext validationContext);
}
