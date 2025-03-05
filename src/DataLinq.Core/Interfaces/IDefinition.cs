using DataLinq.Metadata;

namespace DataLinq.Interfaces;

public interface IDefinition
{
    CsFileDeclaration? CsFile { get; }
}