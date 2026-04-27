using System.Data;

namespace DataLinq.Query;

public sealed record SqlParameterBinding
{
    public SqlParameterBinding(string parameterName, object? value)
    {
        ParameterName = parameterName;
        Value = value;
    }

    private SqlParameterBinding(IDataParameter parameter)
    {
        ParameterName = parameter.ParameterName;
        Value = parameter.Value;
        ProviderParameter = parameter;
    }

    public string ParameterName { get; }

    public object? Value { get; }

    public IDataParameter? ProviderParameter { get; }

    public static SqlParameterBinding FromProviderParameter(IDataParameter parameter)
        => new(parameter);
}
