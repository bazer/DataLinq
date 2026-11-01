using System.Text;

namespace DataLinq.Validation;

public sealed partial class SchemaComparer
{
    // Conservative textual normalization, not SQL semantic equivalence. Quoted
    // strings/identifiers remain exact. Backslash interpretation depends on the
    // server SQL mode, so expressions containing it retain their original text.
    private static string NormalizeRuntimeSql(string sql, bool unwrap)
    {
        if (sql.IndexOf('\\') >= 0) return sql.Trim();
        var text = new StringBuilder();
        var quote = '\0';
        var whitespace = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var value = sql[i];
            if (quote != '\0')
            {
                text.Append(value);
                if (value == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote) text.Append(sql[++i]);
                    else quote = '\0';
                }
                continue;
            }
            // Comments have newline-sensitive or provider-specific semantics.
            // Retaining the original expression is safer than rewriting them.
            if (value == '#' || i + 1 < sql.Length &&
                (value == '-' && sql[i + 1] == '-' || value == '/' && sql[i + 1] == '*')) return sql.Trim();
            if (char.IsWhiteSpace(value)) { whitespace = text.Length > 0; continue; }
            if (whitespace) { text.Append(' '); whitespace = false; }
            text.Append(value);
            if (value is '\'' or '"' or '`') quote = value;
        }
        if (quote != '\0') return sql.Trim();
        var result = text.ToString();
        while (unwrap && Enclosed(result)) result = result.Substring(1, result.Length - 2).Trim();
        return result;
    }

    private static bool Enclosed(string sql)
    {
        if (sql.Length < 2 || sql[0] != '(' || sql[sql.Length - 1] != ')') return false;
        var depth = 0;
        var quote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var value = sql[i];
            if (quote != '\0')
            {
                if (value == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote) i++;
                    else quote = '\0';
                }
                continue;
            }
            if (value is '\'' or '"' or '`') quote = value;
            else if (value == '(') depth++;
            else if (value == ')' && (--depth == 0 && i != sql.Length - 1 || depth < 0)) return false;
        }
        return quote == '\0' && depth == 0;
    }
}
