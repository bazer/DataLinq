using System;

namespace DataLinq.Metadata;

public sealed class ModelFileGenerationException : Exception
{
    public ModelFileGenerationException(ModelDefinition model, Exception innerException)
        : base($"Failed to generate model file for '{model.CsType.Name}': {innerException.Message}", innerException)
    {
        Model = model;
    }

    public ModelDefinition Model { get; }

    public SourceLocation? GetSourceLocation() => Model.GetSourceLocation();
}
