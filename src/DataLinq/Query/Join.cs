using System;

namespace DataLinq.Query;

internal enum JoinType
{
    Inner,
    LeftOuter,
    RightOuter
}

public class Join<T>
{
    public readonly SqlQuery<T> Query;
    readonly string TableName;
    readonly JoinType Type;
    protected WhereGroup<T>? OnClauseContainer = null;
    private readonly string? Alias;

    internal Join(SqlQuery<T> query, string tableName, string? alias, JoinType type)
    {
        this.Query = query;
        this.TableName = tableName;
        this.Type = type;
        this.Alias = alias;
    }

    public SqlQuery<T> On(Action<WhereGroup<T>> buildOnClause)
    {
        // Create a new WhereGroup specifically for this ON clause.
        // Its internal children will be ANDed by default, which is typical for ON clauses.
        this.OnClauseContainer = new WhereGroup<T>(this.Query, BooleanType.And, false);

        // Invoke the user's action to populate this OnClauseContainer
        buildOnClause(this.OnClauseContainer);

        if (this.OnClauseContainer.Length == 0)
            throw new InvalidOperationException($"Join ON clause for table '{TableName}' cannot be empty.");

        return this.Query; // Return the parent SqlQuery<T> instance
    }

    public Sql GetSql(Sql sql, string? paramPrefix)
    {
        if (Type == JoinType.Inner)
            sql.AddText("\nJOIN ");
        else if (Type == JoinType.LeftOuter)
            sql.AddText("\nLEFT JOIN ");
        else if (Type == JoinType.RightOuter)
            sql.AddText("\nRIGHT JOIN ");
        else
            throw new NotImplementedException("Wrong JoinType: " + Type);

        Query.AddTableName(sql, TableName, Alias);
        sql.AddText(" ON ");

        if (OnClauseContainer == null || OnClauseContainer.Length == 0)
            throw new InvalidOperationException($"Join ON clause for table '{TableName}' cannot be empty.");

        // Determine if the ON clause itself needs surrounding parentheses
        // (usually if it's negated - not typical for ON - or has multiple top-level ORs internally)
        // For ON clauses that are typically `A=B AND C=D`, parentheses are often not needed for the whole ON block.
        // Let AddCommandString handle its internal parentheses.
        // Passing 'false' for addParentheses to the root of the ON clause content.
        OnClauseContainer.AddCommandString(sql, paramPrefix, true, false);

        return sql;
    }
}
