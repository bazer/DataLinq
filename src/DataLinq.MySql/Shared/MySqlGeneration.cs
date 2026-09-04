using System;
using DataLinq.Metadata;

namespace DataLinq.MySql;

public class MySqlGeneration : SqlGeneration
{
    private readonly bool noBackslashEscapes;

    public MySqlGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText = "")
        : this(indentationSpaces, quoteChar, generatedText, false)
    {
    }

    public MySqlGeneration(int indentationSpaces, char quoteChar, string generatedText, bool noBackslashEscapes)
        : base(indentationSpaces, quoteChar, generatedText)
    {
        this.noBackslashEscapes = noBackslashEscapes;
    }

    internal static string QuoteString(string value, bool noBackslashEscapes) =>
        "'" + (noBackslashEscapes ? value : value.Replace("\\", "\\\\", StringComparison.Ordinal)).Replace("'", "''", StringComparison.Ordinal) + "'";

    protected override string FormatEnumLiteral(string value) => QuoteString(value, noBackslashEscapes);

    public override SqlGeneration CreateView(string viewName, string definition)
    {
        sql.AddText($"CREATE OR REPLACE VIEW {QuoteCharacter}{viewName}{QuoteCharacter}\n");
        sql.AddText($"AS {definition};");
        sql.AddText("\n\n");
        return this;
    }
}
