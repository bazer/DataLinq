using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Query;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Metadata;


public class SqlGeneration
{
    public SqlGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText = "")
    {
        IndentationSpaces = indentationSpaces;
        QuoteCharacter = quoteChar;
        if (!string.IsNullOrEmpty(generatedText))
            sql.AddText(generatedText.Replace("%datetime%", DateTime.Now.ToString()));
    }

    // Keep the throwing entry points for existing callers; schema factories use
    // the failure-returning entry points to diagnose unsupported dependency cycles.
    public List<TableDefinition> SortTablesByForeignKeys(List<TableDefinition> tables)
        => TrySortTablesByForeignKeys(tables).ValueOrException();

    public Option<List<TableDefinition>, IDLOptionFailure> TrySortTablesByForeignKeys(
        List<TableDefinition> tables, bool allowCycles = false)
        => SortByDependencies(tables, table => table.ColumnIndices
            .Where(index => index.Characteristic == IndexCharacteristic.ForeignKey)
            .SelectMany(index => index.RelationParts)
            .Where(part => part.Type == RelationPartType.ForeignKey)
            .Select(part => part.GetOtherSide().ColumnIndex.Table)
            .Where(other => other != table).Distinct(), allowCycles, "foreign key", table => table.DbName);

    public List<ViewDefinition> SortViewsByForeignKeys(List<ViewDefinition> views)
        => TrySortViewsByForeignKeys(views).ValueOrException();

    public Option<List<ViewDefinition>, IDLOptionFailure> TrySortViewsByForeignKeys(List<ViewDefinition> views)
        => SortByDependencies(views, view => views.Where(other => other != view &&
            view.Definition?.Contains(other.DbName) == true), false, "view", view => view.DbName);

    private static Option<List<T>, IDLOptionFailure> SortByDependencies<T>(List<T> items,
        Func<T, IEnumerable<T>> dependencies, bool allowCycles, string dependencyKind, Func<T, string> name)
        where T : class
    {
        var positions = items.Select((item, index) => (item, index)).ToDictionary(x => x.item, x => x.index);
        var states = new byte[items.Count]; // unseen, visiting, complete
        var ordered = new List<T>(items.Count);
        var path = new List<int>();
        var stack = new Stack<(int Index, IEnumerator<T> Dependencies)>();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (states[i] != 0)
                    continue;
                Visit(i);
                while (stack.Count > 0)
                {
                    var current = stack.Peek();
                    if (!current.Dependencies.MoveNext())
                    {
                        stack.Pop().Dependencies.Dispose();
                        path.RemoveAt(path.Count - 1);
                        states[current.Index] = 2;
                        ordered.Add(items[current.Index]);
                        continue;
                    }
                    // A referenced table outside this script may already exist.
                    if (!positions.TryGetValue(current.Dependencies.Current, out var next))
                        continue;
                    if (states[next] == 0)
                        Visit(next);
                    else if (states[next] == 1 && !allowCycles)
                    {
                        var cycle = path.Skip(path.IndexOf(next)).Append(next).Select(index => name(items[index]));
                        return DLOptionFailure.Fail(DLFailureType.NotImplemented,
                            $"Cyclic {dependencyKind} dependencies cannot be ordered for schema creation: {string.Join(" -> ", cycle)}.");
                    }
                }
            }
            items.Clear();
            items.AddRange(ordered);
            return items;
        }
        finally
        {
            while (stack.Count > 0)
                stack.Pop().Dependencies.Dispose();
        }
        void Visit(int index)
        {
            states[index] = 1;
            path.Add(index);
            stack.Push((index, dependencies(items[index]).GetEnumerator()));
        }
    }

    public int IndentationSpaces { get; set; } = 4;
    public char QuoteCharacter { get; set; } = '`';

    public string Buffer = "";
    List<string> CreateRows { get; set; } = new();

    public Sql sql = new();
    public SqlGeneration NewRow() { if (Buffer != "") CreateRows.Add(Buffer); Buffer = ""; return this; }
    public SqlGeneration Add(string s) { Buffer += s; return this; }
    public SqlGeneration NewLine()
        => Add("\n");

    public SqlGeneration ColumnName(string column) => Add(QuotedString(column));
    public string QuotedString(string s)
        => SqlIdentifier.Quote(s, QuoteCharacter.ToString());
    public SqlGeneration Space()
        => Add(" ");
    public string QuotedParenthesis(string s)
        => $"({QuotedString(s)})";
    public string Parenthesis(string s)
        => $"({s})";
    public string ParenthesisList(string[] columns) =>
        $"{Parenthesis(string.Join(", ", columns.Select(key => QuotedString(key))))}";
    public string ValueWithSpace(string? s)
        => string.IsNullOrWhiteSpace(s) ? " " : $" {s} ";


    //public SqlGeneration CreateDatabase(string databaseName)
    //{
    //    sql.AddText($"CREATE DATABASE IF NOT EXISTS {QuoteCharacter}{databaseName}{QuoteCharacter}; \n");
    //    NewRow();
    //    sql.AddText($"USE {databaseName};\n");

    //    sql.HasCreateDatabase = true;
    //    return this;
    //}

    public virtual SqlGeneration CreateTable(string tableName, Action<SqlGeneration> func) =>
        CreateTable(tableName, func, null);

    public virtual SqlGeneration CreateTable(string tableName, Action<SqlGeneration> func, string? tableOptions)
    {
        sql.AddText($"CREATE TABLE IF NOT EXISTS {QuotedString(tableName)} (\n");
        func(this);
        NewRow();
        sql.AddText(string.Join(",\n", CreateRows.ToArray()));
        CreateRows.Clear();
        sql.AddText("\n)");
        if (!string.IsNullOrWhiteSpace(tableOptions))
            sql.AddText($" {tableOptions}");
        sql.AddText(";\n\n");
        return this;
    }
    public virtual SqlGeneration CreateView(string viewName, string definition)
    {
        sql.AddText($"CREATE VIEW IF NOT EXISTS {QuotedString(viewName)}\n");
        sql.AddText($"AS {definition};");
        sql.AddText("\n\n");
        return this;
    }
    public SqlGeneration Indent()
        => Add(new string(' ', IndentationSpaces));
    public SqlGeneration NewLineComma()
        => Add(",").NewLine();
    public SqlGeneration DefaultValue(string defaultValue) => Space().Add($"DEFAULT {defaultValue}");
    public SqlGeneration Nullable(bool nullable) => Space().Add(nullable ? "NULL" : "NOT NULL");
    public SqlGeneration Autoincrement(bool inc) => inc ? Space().Add("AUTO_INCREMENT") : this;
    public SqlGeneration Type(string type, string columnName, int longestColumnName) => Add(Align(longestColumnName, columnName) + type);
    public SqlGeneration TypeLength(ulong? length, uint? decimals) => length.HasValue
        ? decimals.HasValue
            ? Add($"({length},{decimals})")
            : Add($"({length})")
        : this;
    public SqlGeneration EnumValues(IEnumerable<string> values) => Add($"({string.Join(",", values.Select(FormatEnumLiteral))})");
    protected virtual string FormatEnumLiteral(string value) => $"'{value.Replace("'", "''")}'";
    public SqlGeneration Unsigned(bool? signed) => signed.HasValue && !signed.Value ? Space().Add("UNSIGNED") : this;
    public string Align(int longest, string text) => new string(' ', longest - text.Length);

    public SqlGeneration Index(string name, string? characteristic, string type, params string[] columns)
        => NewRow().Indent().Add($"{(string.IsNullOrWhiteSpace(characteristic) ? "" : $"{characteristic} ")}INDEX {QuotedString(name)} {ParenthesisList(columns)} USING {type}");
    public SqlGeneration PrimaryKey(params string[] columns)
        => NewRow().Indent().Add($"PRIMARY KEY {ParenthesisList(columns)}");
    public SqlGeneration Check(string name, string expression)
        => NewRow().Indent().Add($"CONSTRAINT {QuotedString(name)} CHECK {ParenthesizeCheckExpression(expression)}");
    public virtual SqlGeneration UniqueKey(string name, params string[] columns)
        => NewRow().Indent().Add($"UNIQUE KEY {QuotedString(name)} {ParenthesisList(columns)}");
    public SqlGeneration ForeignKey(RelationPart relation, bool restrict)
        => ForeignKey(
            relation.Relation.ConstraintName == relation.ColumnIndex.Columns[0].DbName ? null : relation.Relation.ConstraintName,
            GetColumnNames(relation.ColumnIndex.Columns),
            relation.Relation.CandidateKey.ColumnIndex.Table.DbName,
            GetColumnNames(relation.Relation.CandidateKey.ColumnIndex.Columns),
            restrict,
            relation.Relation.OnUpdate,
            relation.Relation.OnDelete);
    public SqlGeneration ForeignKey(string? constraintName, string[] from, string table, string[] to, bool restrict)
        => ForeignKey(
            constraintName,
            from,
            table,
            to,
            restrict,
            ReferentialAction.Unspecified,
            ReferentialAction.Unspecified);
    public SqlGeneration ForeignKey(
        string? constraintName,
        string[] from,
        string table,
        string[] to,
        bool restrict,
        ReferentialAction onUpdate,
        ReferentialAction onDelete)
        => NewRow().Indent().Add($"{(string.IsNullOrWhiteSpace(constraintName) ? "" : $"CONSTRAINT {QuotedString(constraintName)} ")}FOREIGN KEY {ParenthesisList(from)} REFERENCES {QuotedString(table)} {ParenthesisList(to)} {OnUpdateDelete(restrict, onUpdate, onDelete)}");
    public string OnUpdateDelete(bool restrict)
        => OnUpdateDelete(restrict, ReferentialAction.Unspecified, ReferentialAction.Unspecified);

    public string OnUpdateDelete(bool restrict, ReferentialAction onUpdate, ReferentialAction onDelete)
    {
        var fallback = restrict ? ReferentialAction.Restrict : ReferentialAction.NoAction;
        var updateAction = onUpdate == ReferentialAction.Unspecified ? fallback : onUpdate;
        var deleteAction = onDelete == ReferentialAction.Unspecified ? fallback : onDelete;

        return $"ON UPDATE {FormatReferentialAction(updateAction)} ON DELETE {FormatReferentialAction(deleteAction)}";
    }

    private static string FormatReferentialAction(ReferentialAction action) =>
        action switch
        {
            ReferentialAction.NoAction => "NO ACTION",
            ReferentialAction.Restrict => "RESTRICT",
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            _ => "NO ACTION"
        };

    private static string[] GetColumnNames(IReadOnlyList<ColumnDefinition> columns)
    {
        var names = new string[columns.Count];
        for (var i = 0; i < names.Length; i++)
            names[i] = columns[i].DbName;

        return names;
    }

    private string ParenthesizeCheckExpression(string expression)
    {
        var trimmed = expression.Trim();

        return trimmed.StartsWith("(") && trimmed.EndsWith(")") && HasBalancedOuterParentheses(trimmed)
            ? trimmed
            : Parenthesis(trimmed);
    }

    private static bool HasBalancedOuterParentheses(string value)
    {
        var depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
                depth--;

            if (depth == 0 && i < value.Length - 1)
                return false;
        }

        return depth == 0;
    }
}
