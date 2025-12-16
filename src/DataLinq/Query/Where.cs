using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Query;

public enum Operator
{
    Equal,
    EqualNull,
    NotEqual,
    NotEqualNull,
    Like,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    In,
    NotIn,
    NotLike,
    AlwaysFalse,
    AlwaysTrue
}

public record Comparison(
    Operand Left,
    Operator Operator,
    Operand Right);

public interface IWhere<T> : IQueryPart
{
}

public class Where<T> : IWhere<T>
{
    internal Operand? Left;
    internal Operand? Right;
    internal Operator Operator;
    internal bool IsNegated = false;
    protected WhereGroup<T> WhereGroup;

    internal Where(WhereGroup<T> group, Operand left, bool isNegated = false)
    {
        WhereGroup = group;
        Left = left;
        IsNegated = isNegated;
    }

    internal Where(WhereGroup<T> group, Operand left, Operator @operator, Operand right, bool isNegated = false)
    {
        WhereGroup = group;
        Left = left;
        Operator = @operator;
        Right = right;
        IsNegated = isNegated;
    }

    internal Where(WhereGroup<T> group, Comparison comparison, bool isNegated = false)
    {
        WhereGroup = group;
        Left = comparison.Left;
        Operator = comparison.Operator;
        Right = comparison.Right;
        IsNegated = isNegated;
    }

    internal Where(WhereGroup<T> group, Operator fixedRelation)
    {
        if (fixedRelation != Operator.AlwaysTrue && fixedRelation != Operator.AlwaysFalse)
            throw new ArgumentException("This constructor is for AlwaysTrue or AlwaysFalse relations only.");

        WhereGroup = group;
        Operator = fixedRelation;
    }

    public WhereGroup<T> EqualTo<V>(V value)
    {
        return SetAndReturn(value, value == null ? Operator.EqualNull : Operator.Equal);
    }

    public WhereGroup<T> EqualToNull()
    {
        return SetAndReturnNull(Operator.EqualNull);
    }

    public WhereGroup<T> EqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Operator.Equal);
    }

    public WhereGroup<T> NotEqualTo<V>(V value)
    {
        return SetAndReturn(value, value == null ? Operator.NotEqualNull : Operator.NotEqual);
    }

    public WhereGroup<T> NotEqualToNull()
    {
        return SetAndReturnNull(Operator.NotEqualNull);
    }

    public WhereGroup<T> NotEqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Operator.NotEqual);
    }

    public WhereGroup<T> EqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Operator.Equal);
    }

    public WhereGroup<T> NotEqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Operator.NotEqual);
    }

    public WhereGroup<T> GreaterThanRaw(string sql)
    {
        return SetAndReturnRaw(sql, Operator.GreaterThan);
    }

    public WhereGroup<T> GreaterThanOrEqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Operator.GreaterThanOrEqual);
    }

    public WhereGroup<T> LessThanRaw(string sql)
    {
        return SetAndReturnRaw(sql, Operator.LessThan);
    }

    public WhereGroup<T> LessThanOrEqualToRaw(string sql)
    {
        return SetAndReturnRaw(sql, Operator.LessThanOrEqual);
    }

    public WhereGroup<T> Like<V>(V value)
    {
        return SetAndReturn(value, Operator.Like);
    }

    public WhereGroup<T> LikeColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Operator.Like);
    }

    public WhereGroup<T> GreaterThan<V>(V value)
    {
        return SetAndReturn(value, Operator.GreaterThan);
    }

    public WhereGroup<T> GreaterThanColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Operator.GreaterThan);
    }

    public WhereGroup<T> GreaterThanOrEqual<V>(V value)
    {
        return SetAndReturn(value, Operator.GreaterThanOrEqual);
    }

    public WhereGroup<T> GreaterThanOrEqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Operator.GreaterThanOrEqual);
    }

    public WhereGroup<T> LessThan<V>(V value)
    {
        return SetAndReturn(value, Operator.LessThan);
    }

    public WhereGroup<T> LessThanColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Operator.LessThan);
    }

    public WhereGroup<T> LessThanOrEqual<V>(V value)
    {
        return SetAndReturn(value, Operator.LessThanOrEqual);
    }

    public WhereGroup<T> LessThanOrEqualToColumn(string column, string? alias = null)
    {
        return SetAndReturnColumn(column, alias, Operator.LessThanOrEqual);
    }

    public WhereGroup<T> In<V>(IEnumerable<V> values) =>
        In(values.ToArray());

    public WhereGroup<T> In<V>(params V[] values)
    {
        return SetAndReturn(values, Operator.In);
    }

    public WhereGroup<T> NotIn<V>(IEnumerable<V> values) =>
        NotIn(values.ToArray());

    public WhereGroup<T> NotIn<V>(params V[] values)
    {
        return SetAndReturn(values, Operator.NotIn);
    }

    protected WhereGroup<T> SetAndReturn<V>(V[] value, Operator @operator)
    {
        Right = Operand.Value(value.Cast<object>());
        Operator = @operator;
        return this.WhereGroup;
    }

    protected WhereGroup<T> SetAndReturn<V>(V? value, Operator @operator)
    {
        Right = Operand.Value(value);
        Operator = @operator;
        return this.WhereGroup;
    }

    protected WhereGroup<T> SetAndReturnNull(Operator @operator)
    {
        Right = Operand.Value([null]);
        Operator = @operator;
        return this.WhereGroup;
    }

    protected WhereGroup<T> SetAndReturnColumn(string column, string? alias, Operator @operator)
    {
        Right = Operand.Column(column, alias);
        Operator = @operator;

        return this.WhereGroup;
    }

    private WhereGroup<T> SetAndReturnRaw(string sql, Operator @operator)
    {
        Right = Operand.RawSql(sql);
        Operator = @operator;

        return WhereGroup;
    }

    public void AddCommandString(Sql sql, string prefix, bool addCommandParameter = true, bool addParentheses = false)
    {
        // Handle fixed conditions first
        if (Operator == Operator.AlwaysTrue || Operator == Operator.AlwaysFalse)
        {
            if (Operator == Operator.AlwaysFalse)
                sql.AddText(IsNegated ? "1=1" : "1=0");
            else if (Operator == Operator.AlwaysTrue)
                sql.AddText(IsNegated ? "1=0" : "1=1");
            return;
        }

        if (Left == null || Right == null)
        {
            throw new InvalidOperationException("Both Left and Right operands must be set before generating SQL.");
        }

        addParentheses = addParentheses || IsNegated;

        if (IsNegated)
            sql.AddText("NOT ");

        if (addParentheses)
            sql.AddText("(");

        var leftSql = GetOperandSql(Left, sql, prefix, addCommandParameter);
        var rightSql = GetOperandSql(Right, sql, prefix, addCommandParameter);

        sql.AddFormat("{0} {1} {2}", leftSql, GetOperatorSql(Left, Operator, Right), rightSql);

        if (addParentheses)
            sql.AddText(")");
    }

    protected Operator GetCorrectedOperator(Operand left, Operator @operator, Operand right)
    {
        // If the operator is EqualNull or NotEqualNull, we need to ensure it is handled correctly
        if ((@operator == Operator.Equal || @operator == Operator.NotEqual) &&
            ((left is ValueOperand valueOperandLeft && valueOperandLeft.IsNull) ||
            right is ValueOperand valueOperandRight && valueOperandRight.IsNull))
        {
            // If one of the operands is null, we can only use IS NULL or IS NOT NULL
            return @operator == Operator.Equal ? Operator.EqualNull : Operator.NotEqualNull;
        }

        // For other operators, we can return them as is
        return @operator;
    }

    protected string GetOperatorSql(Operand left, Operator @operator, Operand right)
    {
        var operatorToUse = GetCorrectedOperator(left, @operator, right);

        // If the operator is AlwaysTrue or AlwaysFalse, we handle it separately
        return WhereGroup.Query.DataSource.Provider.GetOperatorSql(operatorToUse);
    }

    protected string GetOperandSql(Operand operand, Sql sql, string prefix, bool addCommandParameter)
    {
        if (operand is ValueOperand valueOperand)
        {
            // Check for empty IN/NOT IN here before calling provider
            if ((Operator == Operator.In || Operator == Operator.NotIn) && valueOperand.IsNull)
            {
                // Empty list for IN means false, for NOT IN means true
                return Operator == Operator.In ? "1=0" : "1=1";
            }
            else
            {
                var indexList = addCommandParameter ? GetCommandParameter(operand, sql, prefix).ToArray() : ([sql.Index]);
                return WhereGroup.Query.DataSource.Provider.GetParameterName(Operator, indexList.Select(x => prefix + "w" + x).ToArray());
            }
        }
        else if (operand is RawSqlOperand rawOperand)
        {
            return rawOperand.Sql;
        }
        else if (operand is ColumnOperand columnOperand)
            return columnOperand.FormatName(WhereGroup.Query.EscapeCharacter);
        else
            throw new NotSupportedException($"Unsupported operand type: {operand.GetType().Name}");
    }

    protected IEnumerable<int> GetCommandParameter(Operand operand, Sql sql, string prefix)
    {
        if (operand is ValueOperand valueOperand)
        {
            foreach (var value in valueOperand.Values)
            {
                yield return sql.Index;
                WhereGroup.Query.DataSource.Provider.GetParameter(sql, prefix + "w" + sql.IndexAdd(), value);
            }
        }
    }

    public override string ToString()
    {
        var sql = new Sql();
        AddCommandString(sql, "");

        return sql.ToString();
    }
}