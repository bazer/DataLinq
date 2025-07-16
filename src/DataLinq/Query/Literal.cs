using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Mutation;

namespace DataLinq.Query;

public class Literal : IQuery
{
    private readonly string sql;
    private readonly List<IDataParameter> parameters;
    protected DataSourceAccess transaction;
    public DataSourceAccess DataSource => transaction;

    public Literal(DataSourceAccess dataSource, string sql)
    {
        this.transaction = dataSource;
        this.sql = sql;
        this.parameters = new List<IDataParameter>();
    }

    public Literal(DataSourceAccess dataSource, string sql, IEnumerable<IDataParameter> parameters)
    {
        this.transaction = dataSource;
        this.sql = sql;
        this.parameters = parameters.ToList();
    }

    public Literal(DataSourceAccess dataSource, string sql, params IDataParameter[] parameters)
    {
        this.transaction = dataSource;
        this.sql = sql;
        this.parameters = parameters.ToList();
    }

    public Sql ToSql(string? paramPrefix = null)
    {
        return new Sql(sql, parameters.ToArray());
    }

    public IDbCommand ToDbCommand()
    {
        return DataSource.Provider.ToDbCommand(this);
    }

    public int ParameterCount
    {
        get { return parameters.Count(); }
    }
}