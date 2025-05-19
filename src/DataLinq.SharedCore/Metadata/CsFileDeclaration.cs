using System;
using System.IO;

namespace DataLinq.Metadata;

public readonly record struct CsFileDeclaration
{
    public string Name { get; }
    public string FullPath { get; }
    public CsFileDeclaration(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            throw new ArgumentException($"Argument '{nameof(fullPath)}' is null or empty");

        FullPath = fullPath;
        Name = Path.GetFileName(fullPath);
    }

    public override string ToString() => FullPath;
}
