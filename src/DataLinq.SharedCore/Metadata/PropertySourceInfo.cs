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

public readonly struct PropertySourceInfo
{
    public PropertySourceInfo(SourceTextSpan propertySpan, SourceTextSpan? defaultValueExpressionSpan)
    {
        PropertySpan = propertySpan;
        DefaultValueExpressionSpan = defaultValueExpressionSpan;
    }

    public SourceTextSpan PropertySpan { get; }
    public SourceTextSpan? DefaultValueExpressionSpan { get; }
}
