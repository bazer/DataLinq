using System;
using System.Collections.Generic;
using DataLinq.Metadata;

namespace DataLinq.Query;

public enum BooleanType
{
    And,
    Or
}

public class WhereGroup<T> : IWhere<T>
{
    public readonly SqlQuery<T> Query;
    // Stores child conditions/groups and how each connects to the *previous* child in this list.
    // The first child's BooleanType is effectively ignored for connection purposes (it's the start).
    protected List<(IWhere<T> where, BooleanType connectionToPrevious)>? whereList;
    private readonly bool isNegated;
    public bool IsNegated => isNegated;

    public int Length => whereList?.Count ?? 0;
    // Defines how subsequent children in *this* group are joined if not explicitly overridden by an OR.
    public BooleanType InternalJoinType { get; }

    /// <summary>
    /// Initializes a new WhereGroup.
    /// </summary>
    /// <param name="query">The parent SqlQuery.</param>
    /// <param name="internalJoinType">How direct children of THIS group should be joined by default (AND or OR).</param>
    /// <param name="isNegated">If this entire group is negated (e.g., NOT (A OR B)).</param>
    internal WhereGroup(SqlQuery<T> query, BooleanType internalJoinType = BooleanType.And, bool isNegated = false)
    {
        Query = query;
        this.isNegated = isNegated;
        InternalJoinType = internalJoinType; // This dictates how children A and B in (A op B) are joined
    }

    /// <summary>
    /// Renders the SQL for this WHERE group.
    /// </summary>
    public void AddCommandString(Sql sql, string prefix = "", bool addCommandParameter = true, bool addParentheses = false)
    {
        int length = whereList?.Count ?? 0;
        if (length == 0)
        {
            // An empty group that's negated (e.g. !()) is problematic.
            // An empty group not negated is also odd but less of an SQL issue.
            // For now, let's assume it means "true" if not negated, "false" if negated.
            // Or, if it's part of a larger structure, it might effectively be ignored.
            // For safety, an empty group could render as 1=1 (true) or 1=0 (false) if negated.
            // This helps avoid syntax errors if a group is empty due to logic.
            // However, a truly empty WHERE clause part should ideally not be generated.
            // Let's render 1=1 if not negated and empty, and 1=0 if negated and empty.
            // This behavior might need refinement based on how QueryExecutor handles empty WhereGroup.
            sql.AddText(IsNegated ? "1=0" : "1=1"); // An empty group is TRUE, negated empty group is FALSE

            return;
        }

        if (isNegated)
            sql.AddText("NOT ");

        // Parentheses are needed if the group is negated, or if explicitly requested by caller (addParentheses),
        // or if there's more than one item inside this group being joined by AND/OR.
        bool needsOuterParens = isNegated || addParentheses;
        if (needsOuterParens)
            sql.AddText("(");

        for (int i = 0; i < length; i++)
        {
            var (childWhere, connectionToPrevious) = whereList![i];

            if (i != 0) // For items after the first, use their stored connection type
            {
                sql.AddText(connectionToPrevious == BooleanType.And ? " AND " : " OR ");
            }

            // Determine if the child itself (if it's a group) needs parentheses
            bool childNeedsInnerParens = false;
            if (childWhere is WhereGroup<T> childGroup)
            {
                childNeedsInnerParens = childGroup.IsNegated || (childGroup.whereList?.Count > 1);
            }
            childWhere.AddCommandString(sql, prefix, addCommandParameter, childNeedsInnerParens);
        }

        if (needsOuterParens)
            sql.AddText(")");
    }

    // INTERNAL methods for adding to the list, taking the connection type explicitly.
    // The 'connectionType' is how THIS item connects to the PREVIOUS item in THIS group.
    private Where<T> AddWhereInternal(Where<T> where, BooleanType connectionType)
    {
        whereList ??= new List<(IWhere<T> where, BooleanType connectionToPrevious)>();
        whereList.Add((where, connectionType));
        return where;
    }

    private WhereGroup<T> AddSubGroupInternal(WhereGroup<T> group, BooleanType connectionType)
    {
        whereList ??= new List<(IWhere<T> where, BooleanType connectionToPrevious)>();
        whereList.Add((group, connectionType));
        return group;
    }

    internal WhereGroup<T> Where(IEnumerable<(string columnName, object? value)> wheres, BooleanType type = BooleanType.And, string? alias = null)
    {
        return Query.Where(wheres, type, alias);
    }

    // PUBLIC methods for building the query fluently.
    // These decide the 'connectionType' based on context.

    public Where<T> Where(Operand operand)
    {
        // First item in a group is effectively ANDed to the group's start.
        // Subsequent items use the group's InternalJoinType.
        var connection = (whereList == null || whereList.Count == 0) ? BooleanType.And : InternalJoinType;
        return AddWhereInternal(new Where<T>(this, operand), connection);
    }

    public Where<T> Where(string columnName, string? alias = null)
    {
        // First item in a group is effectively ANDed to the group's start.
        // Subsequent items use the group's InternalJoinType.
        var connection = (whereList == null || whereList.Count == 0) ? BooleanType.And : InternalJoinType;
        return AddWhereInternal(new Where<T>(this, Operand.Column(columnName, alias)), connection);
    }

    public WhereGroup<T> AddWhere(Comparison comparison, BooleanType explicitConnectionType, bool isNegated = false)
    {
        // Used by visitor when it knows the explicit connection type (e.g. from an OR)
        AddWhereInternal(new Where<T>(this, comparison, isNegated), explicitConnectionType);
        return this;
    }

    public Where<T> AddWhere(string columnName, string? alias, BooleanType explicitConnectionType, bool isNegated = false)
    {
        // Used by visitor when it knows the explicit connection type (e.g. from an OR)
        return AddWhereInternal(new Where<T>(this, Operand.Column(columnName, alias), isNegated), explicitConnectionType);
    }

    public Where<T> AddWhereNot(string columnName, string? alias, BooleanType explicitConnectionType)
    {
        return AddWhereInternal(new Where<T>(this, Operand.Column(columnName, alias), isNegated: true), explicitConnectionType);
    }

    public Where<T> AddFixedCondition(Operator fixedRelation, BooleanType explicitConnectionType)
    {
        return AddWhereInternal(new Where<T>(this, fixedRelation), explicitConnectionType);
    }

    public WhereGroup<T> AddSubGroup(WhereGroup<T> group, BooleanType explicitConnectionType)
    {
        return AddSubGroupInternal(group, explicitConnectionType);
    }

    // And, Or methods in WhereGroup now add conditions with specific connector types
    public Where<T> And(string columnName, string? alias = null)
    {
        return AddWhereInternal(new Where<T>(this, Operand.Column(columnName, alias)), BooleanType.And);
    }

    public WhereGroup<T> And(Action<WhereGroup<T>> populateAction)
    {
        var newSubGroup = new WhereGroup<T>(this.Query, BooleanType.And);
        populateAction(newSubGroup);
        return AddSubGroupInternal(newSubGroup, BooleanType.And);
    }

    public Where<T> Or(string columnName, string? alias = null)
    {
        return AddWhereInternal(new Where<T>(this, Operand.Column(columnName, alias)), BooleanType.Or);
    }

    public WhereGroup<T> Or(Action<WhereGroup<T>> populateAction)
    {
        var newSubGroup = new WhereGroup<T>(this.Query, BooleanType.And); // Subgroup children are ANDed by default
        populateAction(newSubGroup);
        return AddSubGroupInternal(newSubGroup, BooleanType.Or);
    }

    // --- Methods to pass through to SqlQuery<T> ---
    public SqlQuery<T> Set<V>(string key, V value) => Query.Set(key, value);
    public IEnumerable<T> Select() => Query.Select();
    public QueryResult Delete() => Query.Delete();
    public QueryResult Insert() => Query.Insert();
    public QueryResult Update() => Query.Update();
    public Select<T> SelectQuery() => new Select<T>(Query);
    public Insert<T> InsertQuery() => new Insert<T>(Query);
    public SqlQuery<T> OrderBy(string columnName, string? alias = null, bool ascending = true) => Query.OrderBy(columnName, alias, ascending);
    public SqlQuery<T> OrderBy(ColumnDefinition column, string? alias = null, bool ascending = true) => Query.OrderBy(column, alias, ascending);
    public SqlQuery<T> OrderByDesc(string columnName, string? alias = null) => Query.OrderByDesc(columnName, alias);
    public SqlQuery<T> OrderByDesc(ColumnDefinition column, string? alias = null) => Query.OrderByDesc(column, alias);
    public SqlQuery<T> Limit(int rows) => Query.Limit(rows);
    public Join<T> Join(string tableName, string? alias = null) => Query.Join(tableName, alias);
    public Join<T> LeftJoin(string tableName, string? alias = null) => Query.LeftJoin(tableName, alias);
    public Join<T> RightJoin(string tableName, string? alias = null) => Query.RightJoin(tableName, alias);

    public override string ToString()
    {
        var sql = new Sql();
        AddCommandString(sql);
        return sql.ToString();
    }
}