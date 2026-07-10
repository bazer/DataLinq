using System;

namespace DataLinq.Core.Factories;

// SyntaxParser cannot materialize a user converter Type inside the analyzer
// process. This marker preserves the source declaration until Roslyn semantic
// resolution replaces it with authoritative ColumnScalarMapping metadata.
internal sealed class ScalarConverterSourceAttribute(string converterTypeSyntax) : Attribute
{
    public string ConverterTypeSyntax { get; } = converterTypeSyntax;
}
