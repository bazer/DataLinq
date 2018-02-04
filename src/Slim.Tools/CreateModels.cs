using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;
using Slim.MySql;
using Slim.Extensions;
using Slim.Metadata;

namespace Slim.Tools
{
    class CreateModels
    {
         public void Execute(string dbname, string namespaceName, string path)
        {
            var database = MetadataFromSqlFactory.ParseDatabase(dbname);

            Console.WriteLine($"Database: {dbname}");
            Console.WriteLine($"Table count: {database.Tables.Count}");
            Console.WriteLine($"Writing models to: {path}");

            foreach (var table in database.Tables)
            {
                var file =
                    FileHeader(namespaceName)
                    .Concat(FileContents(table))
                    .Concat(FileFooter())
                    .ToJoinedString("\r\n");


                var filename = $"{path}{Path.DirectorySeparatorChar}{table.Name}.cs";
                Console.WriteLine($"Writing {table} to: {filename}");

                File.WriteAllText(filename, file, Encoding.GetEncoding("iso-8859-1"));
            }
        }

        private IEnumerable<string> FileContents(Table table)
        {
            var tab = "    ";

            yield return $"{tab}public interface {table.Name} : {(table.Type == TableType.Table ? "ITableModel" : "IViewModel")}";
            yield return tab + "{";

            foreach (var c in table.Columns.OrderByDescending(x => x.PrimaryKey).ThenBy(x => x.Name))
            {
                if (c.PrimaryKey)
                    yield return $"{tab}{tab}[PrimaryKey]";

                foreach (var constraint in c.Constraints)
                {
                    if (constraint.Column == c)
                        yield return $"{tab}{tab}[ConstraintTo(\"{constraint.ReferencedColumn.Table.Name}\", \"{constraint.ReferencedColumn.Name}\", \"{constraint.Name}\")]";
                    else
                        yield return $"{tab}{tab}[ConstraintFrom(\"{constraint.Column.Table.Name}\", \"{constraint.Column.Name}\", \"{constraint.Name}\")]";
                }

                if (c.Nullable)
                    yield return $"{tab}{tab}[Nullable]";

                if (c.Length.HasValue)
                    yield return $"{tab}{tab}[Type(\"{c.DbType}\", {c.Length})]";
                else
                    yield return $"{tab}{tab}[Type(\"{c.DbType}\")]";

                //if (c.ForeignKey && !c.Nullable)
                //    yield return $"{tab}{tab}[ForeignKey]";
                //else if (c.ForeignKey && c.Nullable)
                //    yield return $"{tab}{tab}[ForeignKey(nullable: true)]";

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
    }
}
