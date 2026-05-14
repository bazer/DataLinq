namespace DataLinq.Metadata;

public readonly struct SourceLinePosition
{
    public SourceLinePosition(int startLine, int startColumn, int endLine, int endColumn)
    {
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public int StartLine { get; }
    public int StartColumn { get; }
    public int EndLine { get; }
    public int EndColumn { get; }
}

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

public static class SourceLocationFormatter
{
    public static string Format(SourceLocation sourceLocation, string? sourceText = null)
    {
        if (sourceLocation.Span.HasValue &&
            sourceText != null &&
            TryGetLinePosition(sourceText, sourceLocation.Span.Value, out var linePosition))
            return $"{sourceLocation.File.FullPath}:{linePosition.StartLine}:{linePosition.StartColumn}";

        return sourceLocation.File.FullPath;
    }

    public static bool TryGetLinePosition(string sourceText, SourceTextSpan span, out SourceLinePosition linePosition)
    {
        if (sourceText == null ||
            span.Start < 0 ||
            span.Start > sourceText.Length ||
            span.Length < 0)
        {
            linePosition = default;
            return false;
        }

        var end = span.End;
        if (end < span.Start ||
            end > sourceText.Length)
        {
            linePosition = default;
            return false;
        }

        var start = GetPosition(sourceText, span.Start);
        var exclusiveEnd = GetPosition(sourceText, end);
        linePosition = new SourceLinePosition(
            start.Line,
            start.Column,
            exclusiveEnd.Line,
            exclusiveEnd.Column);
        return true;
    }

    private static (int Line, int Column) GetPosition(string sourceText, int offset)
    {
        var line = 1;
        var column = 1;

        for (var i = 0; i < offset; i++)
        {
            var character = sourceText[i];
            if (character == '\r')
            {
                if (i + 1 < offset &&
                    sourceText[i + 1] == '\n')
                    i++;

                line++;
                column = 1;
                continue;
            }

            if (character == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return (line, column);
    }
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
