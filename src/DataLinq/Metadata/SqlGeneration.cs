using DataLinq.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Metadata
{
    
    public class SqlGeneration
    {
        public SqlGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText = "")
        {
            IndentationSpaces = indentationSpaces;
            QuoteCharacter = quoteChar;
            if (!string.IsNullOrEmpty(generatedText))
                sql.AddText(generatedText.Replace("%datetime%", DateTime.Now.ToString()));
        }

        // Sort tables when generating SQL code to ensure that tables with foreign key columns are created after the candidate key tables.
        public List<TableMetadata> SortTablesByForeignKeys(List<TableMetadata> tables)
        {
            for (var i = 0; i < tables.Count; i++)
            {
                var table = tables[i];
                foreach (var fk in table.Columns.Where(x => x.ForeignKey))
                {
                    var fkIndex = tables.IndexOf(fk.RelationParts.First().Relation.CandidateKey.Column.Table);
                    var fkTable = tables[fkIndex];
                    if (fkIndex > i)
                    {
                        tables[i] = fkTable;
                        tables[fkIndex] = table;
                        return SortTablesByForeignKeys(tables);
                    }
                }
            }
            return tables;
        }

        public List<ViewMetadata> SortViewsByForeignKeys(List<ViewMetadata> views)
        {
            for (var i = 0; i < views.Count; i++)
            {
                var view = views[i];

                foreach (var fkView in views.Where(x => x.Definition?.Contains(view.DbName) == true))
                {
                    var fkIndex = views.IndexOf(fkView);
                    //var fkTable = views[fkIndex];
                    if (fkIndex < i)
                    {
                        views[i] = fkView;
                        views[fkIndex] = view;
                        return SortViewsByForeignKeys(views);
                    }
                }

            }
            return views;
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
            => $"{QuoteCharacter}{s}{QuoteCharacter}";
        public SqlGeneration Space()
            => Add(" ");
        public string QuotedParenthesis(string s)
            => $"({QuotedString(s)})";
        public string Parenthesis(string s)
            => $"({s})";
        public string ParenthesisList(string[] columns) =>
            $"{Parenthesis(string.Join(", ", columns.Select(key => QuotedString(key))))}";

        //public SqlGeneration CreateDatabase(string databaseName)
        //{
        //    sql.AddText($"CREATE DATABASE IF NOT EXISTS {QuoteCharacter}{databaseName}{QuoteCharacter}; \n");
        //    NewRow();
        //    sql.AddText($"USE {databaseName};\n");

        //    sql.HasCreateDatabase = true;
        //    return this;
        //}

        public SqlGeneration CreateTable(string tableName, Action<SqlGeneration> func)
        {
            sql.AddText($"CREATE TABLE IF NOT EXISTS {QuoteCharacter}{tableName}{QuoteCharacter} (\n");
            func(this);
            NewRow();
            sql.AddText(string.Join(",\n", CreateRows.ToArray()));
            CreateRows.Clear();
            sql.AddText("\n);\n\n");
            return this;
        }
        public SqlGeneration CreateView(string viewName, string definition)
        {
            sql.AddText($"CREATE VIEW IF NOT EXISTS {QuoteCharacter}{viewName}{QuoteCharacter}\n");
            sql.AddText($"AS {definition};");
            sql.AddText("\n\n");
            return this;
        }
        public SqlGeneration Indent()
            => Add(new string(' ', IndentationSpaces));
        public SqlGeneration NewLineComma()
            => Add(",").NewLine();
        public SqlGeneration Nullable(bool nullable) => Space().Add(nullable ? "NULL" : "NOT NULL");
        public SqlGeneration Autoincrement(bool inc) => inc ? Space().Add("AUTO_INCREMENT") : this;
        public SqlGeneration Type(string type, string columnName, int longestColumnName) => Add(Align(longestColumnName, columnName) + type);
        public SqlGeneration TypeLength(long? length) => length.HasValue ? Add($"({length})") : this;
        public SqlGeneration EnumValues(IEnumerable<string> values) => Add($"({string.Join(",", values.Select(x => $"'{x}'"))})");
        public SqlGeneration Unsigned(bool? signed) => signed.HasValue && !signed.Value ? Space().Add("UNSIGNED") : this;
        public string Align(int longest, string text) => new string(' ', longest - text.Length);

        public SqlGeneration Index(string name, params string[] columns)
            => NewRow().Indent().Add($"INDEX {QuotedString(name)} {ParenthesisList(columns)}");
        public SqlGeneration PrimaryKey(params string[] columns)
            => NewRow().Indent().Add($"PRIMARY KEY {ParenthesisList(columns)}");
        public virtual SqlGeneration UniqueKey(string name, params string[] columns)
            => NewRow().Indent().Add($"UNIQUE KEY {QuotedString(name)} {ParenthesisList(columns)}");
        public SqlGeneration ForeignKey(RelationPart relation, bool restrict)
            => ForeignKey(relation.Relation.ConstraintName, relation.Column.DbName, relation.Relation.CandidateKey.Column.Table.DbName, relation.Relation.CandidateKey.Column.DbName, restrict);
        public SqlGeneration ForeignKey(string constraint, string from, string table, string to, bool restrict)
            => NewRow().Indent().Add($"CONSTRAINT {QuotedString(constraint)} FOREIGN KEY {QuotedParenthesis(from)} REFERENCES {QuotedString(table)} {QuotedParenthesis(to)} {OnUpdateDelete(restrict)}");
        public string OnUpdateDelete(bool restrict)
            => restrict ? "ON UPDATE RESTRICT ON DELETE RESTRICT" : "ON UPDATE NO ACTION ON DELETE NO ACTION";
    }
}