using System;
using System.Data;
using DataLinq.Query;

namespace DataLinq.Execution;

/// <summary>
/// Private statement/parameter snapshot. Array values follow the existing shallow-array-copy
/// contract; arbitrary scalar objects are not deep-cloned. Provider parameters must explicitly
/// support independent cloning so provider-specific metadata is not silently discarded.
/// </summary>
internal sealed class CapturedSql : IQuery
{
    private readonly SqlParameterBinding[] parameters;
    internal string Text { get; }

    private CapturedSql(string text, SqlParameterBinding[] parameters)
    {
        Text = text;
        this.parameters = parameters;
    }

    internal static CapturedSql Capture(Sql sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var text = sql.Text;
        var parameters = new SqlParameterBinding[sql.Parameters.Count];
        for (var i = 0; i < parameters.Length; i++) parameters[i] = Copy(sql.Parameters[i]);
        return new(text, parameters);
    }

    // Like Literal, this statement is already rendered; parameter prefixes cannot rebind it.
    public Sql ToSql(string? paramPrefix = null)
    {
        var sql = new Sql(Text);
        foreach (var parameter in parameters) sql.Parameters.Add(Copy(parameter));
        return sql;
    }

    private static SqlParameterBinding Copy(SqlParameterBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.ProviderParameter is not { } parameter)
            return new(binding.ParameterName, CopyValue(binding.Value));
        if (parameter is not ICloneable cloneable || cloneable.Clone() is not IDataParameter copy || ReferenceEquals(copy, parameter))
            throw new NotSupportedException("A captured provider parameter must supply an independent IDataParameter clone.");
        copy.Value = CopyValue(parameter.Value);
        return SqlParameterBinding.FromProviderParameter(copy);
    }

    private static object? CopyValue(object? value) => value is Array array ? array.Clone() : value;
}
