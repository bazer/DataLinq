using System.Collections.Generic;
using DataLinq.Metadata;

namespace DataLinq.SourceGenerators;

internal sealed class GeneratorValidationContext
{
    private readonly HashSet<ValueProperty> suppressedDefaultValueProperties = new();

    public IReadOnlyCollection<ValueProperty> SuppressedDefaultValueProperties => suppressedDefaultValueProperties;
    internal HashSet<RelationProperty> AsyncNavigationOverrides { get; } = new();
    internal bool StopGeneration { get; set; }

    public void SuppressDefaultValue(ValueProperty property)
    {
        suppressedDefaultValueProperties.Add(property);
    }
}
