﻿using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Query;

public class Sql
{
    private readonly StringBuilder builder = new StringBuilder();
    public List<IDataParameter> Parameters = new List<IDataParameter>();
    public int Index { get; protected set; } = 0;
    public string Text { get { return builder.ToString(); } }
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
        AddText(text);
        AddParameters(parameters);
    }

    public Sql AddParameters(params IDataParameter[] parameters)
    {
        Parameters.AddRange(parameters);

        return this;
    }

    public Sql AddText(string text)
    {
        builder.Append(text);

        return this;
    }

    public Sql AddLineBreak()
    {
        builder.Append('\n');

        return this;
    }

    public Sql AddFormat(string format, params string[] values)
    {
        builder.AppendFormat(format, values);

        return this;
    }

    public Sql Join(string separator, params string[] values)
    {
        int length = values.Length;

        for (int i = 0; i < length; i++)
        {
            builder.Append(values[i]);

            if (i + 1 < length)
                builder.Append(separator);
        }

        return this;
    }

    public Sql AddWhereText(string format, params string[] values)
    {
        builder.AppendFormat(format, values);

        return this;
    }

    public override string ToString()
    {
        return $"{Text}\n{Parameters.Select(x => x.Value).ToJoinedString("\n")}";
    }
}
