using DataLinq.Query;
using System;
using System.Collections.Generic;

namespace DataLinq.Metadata
{
    public class SqlGeneration
    {
        public SqlGeneration(int indentationSpaces = 4, char quoteChar = '`', string generatedText="")
        {
            IndentationSpaces=indentationSpaces;
            QuoteCharacter = quoteChar;
            if (!string.IsNullOrEmpty(generatedText))
                sql.AddText(generatedText.Replace("%datetime%", DateTime.Now.ToString()));
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
        public string QuotedParanthesis(string s)
            => $"({QuotedString(s)})";
        public SqlGeneration CreateTable(string tableName, Action<SqlGeneration> func)
        {
            sql.AddText($"CREATE TABLE {QuoteCharacter}{tableName}{QuoteCharacter} (\r\n");
            func(this);
            NewRow();
            sql.AddText(string.Join(",\r\n", CreateRows.ToArray()));
            CreateRows.Clear();
            sql.AddText("\r\n);\r\n\r\n");
            return this;
        }
        public SqlGeneration Indent() 
            => Add(new string(' ', IndentationSpaces));
        public SqlGeneration NewLineComma() 
            => Add(",").NewLine();
        public SqlGeneration Nullable(bool nullable) => Space().Add(nullable ? "NULL" : "NOT NULL");
        public SqlGeneration Autoincrement(bool inc) => inc ? Space().Add("AUTOINCREMENT") : this;
        public SqlGeneration Type(string type, string columnName, int longestColumnName) => Add(Align(longestColumnName, columnName)+type);
        public SqlGeneration TypeLength(long? length) => length.HasValue ? Add($"({length})") : this;
        public SqlGeneration Unsigned(bool unsigned) => unsigned ? Space().Add("UNSIGNED") : this;
        public string Align(int longest, string text) => new string(' ', longest-text.Length);

        public SqlGeneration Index(string index, string column) => NewRow().Indent().Add($"INDEX {QuotedString(index)} {QuotedParanthesis(column)}");
        public SqlGeneration PrimaryKey(string key) 
            => NewRow().Indent().Add($"PRIMARY KEY {QuotedParanthesis(key)}");
        public SqlGeneration ForeignKey(RelationPart relation, bool restrict)
            => ForeignKey(relation.Relation.Constraint, relation.Column.DbName, relation.Relation.CandidateKey.Column.Table.DbName, relation.Relation.CandidateKey.Column.DbName, restrict); 
        public SqlGeneration ForeignKey(string constraint, string from, string table, string to, bool restrict)
            => NewRow().Indent().Add($"CONSTRAINT {QuotedString(constraint)} FOREIGN KEY {QuotedParanthesis(from)} REFERENCES {QuotedString(table)} {QuotedParanthesis(to)} {OnUpdateDelete(restrict)}");
        public string OnUpdateDelete(bool restrict) 
            => restrict ? "ON UPDATE RESTRICT ON DELETE RESTRICT" : "ON UPDATE NO ACTION ON DELETE NO ACTION";
    }
}