namespace DataLinq.Metadata;

public readonly struct SourceTextSpan
{
    public SourceTextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;
}

public readonly struct SourceLocation
{
    public SourceLocation(CsFileDeclaration file, SourceTextSpan? span = null)
    {
        File = file;
        Span = span;
    }

    public CsFileDeclaration File { get; }
    public SourceTextSpan? Span { get; }

    public override string ToString()
        => Span.HasValue
            ? $"{File.FullPath} [{Span.Value.Start}..{Span.Value.End})"
            : File.FullPath;
}

public readonly struct PropertySourceInfo
{
    public PropertySourceInfo(SourceTextSpan propertySpan, SourceTextSpan? defaultValueExpressionSpan)
    {
        PropertySpan = propertySpan;
        DefaultValueExpressionSpan = defaultValueExpressionSpan;
    }

    public SourceTextSpan PropertySpan { get; }
    public SourceTextSpan? DefaultValueExpressionSpan { get; }

    public SourceLocation GetPropertyLocation(CsFileDeclaration file) => new(file, PropertySpan);

    public SourceLocation? GetDefaultValueLocation(CsFileDeclaration file)
        => DefaultValueExpressionSpan.HasValue
            ? new SourceLocation(file, DefaultValueExpressionSpan.Value)
            : null;
}
