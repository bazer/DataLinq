using DataLinq.Metadata;
using Microsoft.CodeAnalysis;

namespace DataLinq.SourceGenerators;

internal interface IGeneratorDatabaseValidator
{
    void Validate(DatabaseDefinition database, Compilation compilation, SourceProductionContext context, GeneratorValidationContext validationContext);
}
