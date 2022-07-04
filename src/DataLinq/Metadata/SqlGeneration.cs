using DataLinq.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Metadata
{
    public interface ISqlFromMetadataFactory
    {
        public Sql GenerateSql(DatabaseMetadata metadata, bool foreignKeyRestrict);
    }

    public class SqlGeneration
    {
        public SqlGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText="")
        {
            IndentationSpaces=indentationSpaces;
            QuoteCharacter = quoteChar;
            if (!string.IsNullOrEmpty(generatedText))
                sql.AddText(generatedText.Replace("%datetime%", DateTime.Now.ToString()));
        }

        // Sort tables when generating SQL code to ensure that tables with foreign key columns are created after the candidate key tables.
        public List<TableMetadata> SortTablesByForeignKeys(List<TableMetadata> tables)
        {
            for(var i=0;i<tables.Count;i++)
            {
                var table = tables[i];
                foreach(var fk in table.Columns.Where(x => x.ForeignKey))
                {
                    var fkIndex = tables.IndexOf(fk.RelationParts.First().Relation.CandidateKey.Column.Table);
                    var fkTable = tables[fkIndex];
                    if(fkIndex > i)
                    {
                        tables[i] = fkTable;
                        tables[fkIndex] = table;
                        return SortTablesByForeignKeys(tables);
                    }
                }
            }
            return tables;
        }

        public int IndentationSpaces { get; set; } = 4;
        public char QuoteCharacter { get; set; } = '`';

        public string Buffer = "";
        List<string> CreateRows { get; set; } = new();

        public Sql sql = new();
        public SqlGeneration NewRow() { if(Buffer != "") CreateRows.Add(Buffer); Buffer=""; return this; }
        public SqlGeneration Add(string s) { Buffer+=s; return this; }
        public SqlGeneration NewLine()
            => Add("\r\n");

        public SqlGeneration ColumnName(string column) => Add(QuotedString(column)); 
        public string QuotedString(string s)
            => $"{QuoteCharacter}{s}{QuoteCharacter}";
        public SqlGeneration Space()
            => Add(" ");
        public string QuotedParenthesis(string s)
            => $"({QuotedString(s)})";
        public string Parenthesis(string s)
            => $"({s})";
        public SqlGeneration CreateTable(string tableName, Action<SqlGeneration> func)
        {
            sql.AddText($"CREATE TABLE IF NOT EXISTS {QuoteCharacter}{tableName}{QuoteCharacter} (\r\n");
            func(this);
            NewRow();
            sql.AddText(string.Join(",\r\n", CreateRows.ToArray()));
            CreateRows.Clear();
            sql.AddText("\r\n);\r\n\r\n");
            return this;
        }
        public SqlGeneration CreateView(string viewName, string definition)
        {
            sql.AddText($"CREATE VIEW IF NOT EXISTS {QuoteCharacter}{viewName}{QuoteCharacter}\r\n");
            sql.AddText($"AS {definition};");
            sql.AddText("\r\n\r\n");
            return this;
        }
        public SqlGeneration Indent() 
            => Add(new string(' ', IndentationSpaces));
        public SqlGeneration NewLineComma() 
            => Add(",").NewLine();
        public SqlGeneration Nullable(bool nullable) => Space().Add(nullable ? "NULL" : "NOT NULL");
        public SqlGeneration Autoincrement(bool inc) => inc ? Space().Add("AUTO_INCREMENT") : this;
        public SqlGeneration Type(string type, string columnName, int longestColumnName) => Add(Align(longestColumnName, columnName)+type);
        public SqlGeneration TypeLength(long? length) => length.HasValue ? Add($"({length})") : this;
        public SqlGeneration Unsigned(bool? signed) => signed.HasValue && !signed.Value ? Space().Add("UNSIGNED") : this;
        public string Align(int longest, string text) => new string(' ', longest-text.Length);

        public SqlGeneration Index(string index, string column) => NewRow().Indent().Add($"INDEX {QuotedString(index)} {QuotedParenthesis(column)}");
        public SqlGeneration PrimaryKey(params string[] keys) 
            => NewRow().Indent().Add($"PRIMARY KEY {Parenthesis(string.Join(", ", keys.Select(key => QuotedString(key))))}");
        public SqlGeneration ForeignKey(RelationPart relation, bool restrict)
            => ForeignKey(relation.Relation.ConstraintName, relation.Column.DbName, relation.Relation.CandidateKey.Column.Table.DbName, relation.Relation.CandidateKey.Column.DbName, restrict); 
        public SqlGeneration ForeignKey(string constraint, string from, string table, string to, bool restrict)
            => NewRow().Indent().Add($"CONSTRAINT {QuotedString(constraint)} FOREIGN KEY {QuotedParenthesis(from)} REFERENCES {QuotedString(table)} {QuotedParenthesis(to)} {OnUpdateDelete(restrict)}");
        public string OnUpdateDelete(bool restrict) 
            => restrict ? "ON UPDATE RESTRICT ON DELETE RESTRICT" : "ON UPDATE NO ACTION ON DELETE NO ACTION";
    }
}