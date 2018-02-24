using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Slim.Extensions;

namespace Slim.Metadata
{
    public static class FileFactory
    {
        static string tab = "    ";

        public static IEnumerable<(string path, string contents)> CreateModelFiles(Database database, string namespaceName)
        {
            var dbName = database.Tables.Any(x => x.Name == database.Name) 
                ? $"{database.Name}Db"
                : database.Name;

            yield return ($"{dbName}.cs",
                    FileHeader(namespaceName)
                    .Concat(DatabaseFileContents(database, dbName))
                    .Concat(FileFooter())
                    .ToJoinedString("\r\n"));

            foreach (var table in database.Tables)
            {
                var file =
                    FileHeader(namespaceName)
                    .Concat(ModelFileContents(table))
                    .Concat(FileFooter())
                    .ToJoinedString("\r\n");

                var path = table.Type == TableType.Table
                    ? $"Tables{Path.DirectorySeparatorChar}{table.Name}.cs"
                    : $"Views{Path.DirectorySeparatorChar}{table.Name}.cs";

                yield return (path, file);

                //var filename = $"{path}{Path.DirectorySeparatorChar}{table.Name}.cs";
                //Console.WriteLine($"Writing {table} to: {filename}");

                //File.WriteAllText(filename, file, Encoding.GetEncoding("iso-8859-1"));
            }
        }

        private static IEnumerable<string> DatabaseFileContents(Database database, string dbName)
        {
            yield return $"{tab}[Name(\"{database.Name}\")]";
            yield return $"{tab}public interface {dbName} : IDatabaseModel";
            yield return tab + "{";

            foreach (var t in database.Tables.OrderBy(x => x.Name))
            {
                yield return $"{tab}{tab}DbRead<{t.Name}> {t.Name} {{ get; }}";
            }

            yield return tab + "}";
        }

        private static IEnumerable<string> ModelFileContents(Table table)
        {
            yield return $"{tab}[Name(\"{table.Name}\")]";
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

                yield return $"{tab}{tab}{c.CsTypeName}{(c.CsNullable ? "?" : "")} {c.Name} {{ get; }}";
                yield return $"";
            }

            yield return tab + "}";
        }

        private static IEnumerable<string> FileFooter()
        {
            yield return "}";
        }

        private static IEnumerable<string> FileHeader(string namespaceName)
        {
            yield return "using System;";
            yield return "using Slim;";
            yield return "using Slim.Interfaces;";
            yield return "using Slim.Attributes;";
            yield return "";
            yield return $"namespace {namespaceName}";
            yield return "{";
        }
    }
}
