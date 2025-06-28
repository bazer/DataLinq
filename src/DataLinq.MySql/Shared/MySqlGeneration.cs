using DataLinq.Metadata;

namespace DataLinq.MySql;

public class MySqlGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText = "") : SqlGeneration(indentationSpaces, quoteChar, generatedText)
{
    public override SqlGeneration CreateView(string viewName, string definition)
    {
        sql.AddText($"CREATE OR REPLACE VIEW {QuoteCharacter}{viewName}{QuoteCharacter}\n");
        sql.AddText($"AS {definition};");
        sql.AddText("\n\n");
        return this;
    }
}
