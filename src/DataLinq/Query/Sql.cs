using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Query;

public class Sql
{
    private StringBuilder? builder;
    private string? text;
    public List<SqlParameterBinding> Parameters = new List<SqlParameterBinding>();
    public int Index { get; protected set; } = 0;
    public string Text => text ?? builder?.ToString() ?? string.Empty;
    //public bool HasCreateDatabase { get; set; }

    public Sql()
    {
    }

    public int IndexAdd()
    {
        return Index++;
    }

    public Sql(string text, params IDataParameter[] parameters)
    {
        this.text = text;
        AddParameters(parameters);
    }

    public Sql AddParameters(params IDataParameter[] parameters)
    {
        Parameters.AddRange(parameters.Select(SqlParameterBinding.FromProviderParameter));

        return this;
    }

    public Sql AddParameter(string parameterName, object? value)
    {
        Parameters.Add(new SqlParameterBinding(parameterName, value));

        return this;
    }

    public Sql AddText(string text)
    {
        Builder.Append(text);

        return this;
    }

    public Sql AddLineBreak()
    {
        Builder.Append('\n');

        return this;
    }

    public Sql AddFormat(string format, params string[] values)
    {
        Builder.AppendFormat(format, values);

        return this;
    }

    public Sql Join(string separator, params string[] values)
    {
        int length = values.Length;

        for (int i = 0; i < length; i++)
        {
            Builder.Append(values[i]);

            if (i + 1 < length)
                Builder.Append(separator);
        }

        return this;
    }

    public Sql AddWhereText(string format, params string[] values)
    {
        Builder.AppendFormat(format, values);

        return this;
    }

    private StringBuilder Builder
    {
        get
        {
            if (builder is not null)
                return builder;

            builder = text is null
                ? new StringBuilder()
                : new StringBuilder(text);
            text = null;
            return builder;
        }
    }

    public override string ToString()
    {
        return $"{Text}\n{Parameters.Select(x => x.Value).ToJoinedString("\n")}";
    }
}
