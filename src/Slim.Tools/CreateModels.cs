using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;
using Slim.MySql;
using Slim.Extensions;

namespace Slim.Tools
{
    class CreateModels
    {
        public enum TableType
        {
            Table,
            View
        }

        public void Execute(string dbname, string namespaceName, string path)
        {
            var tables = GetTables(dbname).ToList();

            Console.WriteLine($"Database: {dbname}");
            Console.WriteLine($"Table count: {tables.Count}");
            Console.WriteLine($"Writing models to: {path}");

            foreach (var table in tables)
            {
                Console.WriteLine(table);

                var columns = GetColumns(dbname, table.name).ToList();

                var file =
                    FileHeader(namespaceName)
                    .Concat(FileContents(table.name, table.type, columns))
                    .Concat(FileFooter())
                    .ToJoinedString("\r\n");


                var filename = $"{path}{Path.DirectorySeparatorChar}{table.name}.cs";
                Console.WriteLine($"Writing {table} to: {filename}");

                File.WriteAllText(filename, file, Encoding.GetEncoding("iso-8859-1"));
            }
        }

        private IEnumerable<string> FileContents(string tableName, TableType type, List<ColumnDescription> columns)
        {
            var tab = "    ";

            yield return $"{tab}public interface {tableName} : {(type == TableType.Table ? "ITableModel" : "IViewModel")}";
            yield return tab + "{";

            foreach (var c in columns.OrderByDescending(x => x.ForeignKey).ThenBy(x => x.Name))
            {
                if (c.PrimaryKey)
                    yield return $"{tab}{tab}[PrimaryKey]";
                if (c.ForeignKey && !c.Nullable)
                    yield return $"{tab}{tab}[ForeignKey]";
                else if (c.ForeignKey && c.Nullable)
                    yield return $"{tab}{tab}[ForeignKey(nullable: true)]";

                yield return $"{tab}{tab}{c.CsType}{(c.CsNullable ? "?" : "")} {c.Name} {{ get; }}";
                yield return $"";
            }

            yield return tab + "}";
        }

        private IEnumerable<string> FileFooter()
        {
            yield return "}";
        }

        private IEnumerable<string> FileHeader(string namespaceName)
        {
            yield return "using System;";
            yield return "using Slim.Interfaces;";
            yield return "using Slim.Attributes;";
            yield return "";
            yield return $"namespace {namespaceName}";
            yield return "{";
        }

        private IEnumerable<ColumnDescription> GetColumns(string databaseName, string tableName)
        {
            return new MySqlCommand($"describe `{databaseName}`.`{tableName}`")
                .ReadReader()
                .Select(x => new ColumnDescription(x));
        }

        private IEnumerable<(string name, TableType type)> GetTables(string databaseName)
        {
            return new MySqlCommand($"show full tables from `{databaseName}`")
                .ReadReader()
                .Select(x => (x.GetString(0), x.GetString(1) == "BASE TABLE" ? TableType.Table : TableType.View));
        }

        public class ColumnDescription
        {
            public string Default { get; set; }
            public bool ForeignKey { get; set; }
            public int Length { get; set; }
            public string Name { get; set; }
            public bool Nullable { get; set; }
            public bool PrimaryKey { get; set; }
            public string DbType { get; set; }
            public string CsType { get; set; }
            public bool CsNullable { get; set; }

            public ColumnDescription(MySqlDataReader reader)
            {
                Name = reader.GetString(0);

                var combinedType = ReadType(reader.GetString(1));
                DbType = combinedType.type;
                Length = combinedType.length;

                Nullable = reader.GetString(2) == "YES";

                var key = reader.GetString(3);

                if (key == "PRI")
                    PrimaryKey = true;
                else if (key == "MUL")
                    ForeignKey = true;

                if (reader[4] != DBNull.Value)
                    Default = reader.GetString(4);

                CsType = ParseCsType(DbType);
                CsNullable = Nullable && !ForeignKey && IsCsTypeNullable(CsType);
            }

            private (string type, int length) ReadType(string type)
            {
                if (type.Contains("("))
                {
                    var split = type.Split('(');

                    return (split[0], 0);
                }

                return (type, 0);
            }

            private string ParseCsType(string dbType)
            {
                switch (dbType)
                {
                    case "int":
                        return "int";
                    case "tinyint":
                        return "int";
                    case "varchar":
                        return "string";
                    case "text":
                        return "string";
                    case "mediumtext":
                        return "string";
                    case "bit":
                        return "bool";
                    case "double":
                        return "double";
                    case "datetime":
                        return "DateTime";
                    case "date":
                        return "DateTime";
                    case "float":
                        return "float";
                    case "bigint":
                        return "long";
                    case "char":
                        return "Guid";
                    case "binary":
                        return "Guid";
                    case "enum":
                        return "int";
                    default:
                        throw new NotImplementedException($"Unknown type '{dbType}'");
                }
            }

            private bool IsCsTypeNullable(string csType)
            {
                switch (csType)
                {
                    case "int":
                        return true;
                    case "string":
                        return false;
                    case "bool":
                        return true;
                    case "double":
                        return true;
                    case "DateTime":
                        return true;
                    case "float":
                        return true;
                    case "long":
                        return true;
                    case "Guid":
                        return true;
                    default:
                        throw new NotImplementedException($"Unknown type '{csType}'");
                }
            }
        }
    }
}
