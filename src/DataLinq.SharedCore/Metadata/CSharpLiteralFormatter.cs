using System.Globalization;
using System.Text;

namespace DataLinq.Metadata;

internal static class CSharpLiteralFormatter
{
    public static string FormatString(string value) =>
        $"\"{Escape(value, quote: '\"')}\"";

    public static string FormatChar(char value) =>
        $"'{Escape(value, quote: '\'')}'";

    private static string Escape(string value, char quote)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(Escape(character, quote));

        return builder.ToString();
    }

    private static string Escape(char character, char quote)
    {
        if (character == quote)
            return "\\" + character;

        return character switch
        {
            '\\' => "\\\\",
            '\0' => "\\0",
            '\a' => "\\a",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\v' => "\\v",
            _ when char.IsControl(character) => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
            _ => character.ToString()
        };
    }
}
