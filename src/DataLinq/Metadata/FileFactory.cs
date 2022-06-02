using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.Extensions;

namespace DataLinq.Metadata
{
    public class FileFactorySettings
    {
        public string NamespaceName { get; set; } = "Models";
        public bool UseRecords { get; set; } = true;
        public bool UseCache { get; set; } = true;
    }

    public static class FileFactory
    {
        static readonly string tab = "    ";

        public static IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseMetadata database, FileFactorySettings settings)
        {
            var dbName = database.Tables.Any(x => x.DbName == database.Name)
                ? $"{database.Name}Db"
                : database.Name;

            yield return ($"{dbName}.cs",
                    FileHeader(settings.NamespaceName)
                    .Concat(DatabaseFileContents(database, dbName, settings))
                    .Concat(FileFooter())
                    .ToJoinedString("\r\n"));

            foreach (var table in database.Tables)
            {
                var file =
                    FileHeader(settings.NamespaceName)
                    .Concat(ModelFileContents(table, settings))
                    .Concat(FileFooter())
                    .ToJoinedString("\r\n");

                var path = table.Type == TableType.Table
                    ? $"Tables{Path.DirectorySeparatorChar}{table.DbName}.cs"
                    : $"Views{Path.DirectorySeparatorChar}{table.DbName}.cs";

                yield return (path, file);
            }
        }

        private static IEnumerable<string> DatabaseFileContents(DatabaseMetadata database, string dbName, FileFactorySettings settings)
        {
            if (settings.UseCache)
                yield return $"{tab}[UseCache]";

            yield return $"{tab}[Database(\"{database.Name}\")]";
            yield return $"{tab}public interface {dbName} : IDatabaseModel";
            yield return tab + "{";

            foreach (var t in database.Tables.OrderBy(x => x.DbName))
            {
                yield return $"{tab}{tab}DbRead<{t.DbName}> {t.DbName} {{ get; }}";
            }

            yield return tab + "}";
        }

        private static IEnumerable<string> ModelFileContents(TableMetadata table, FileFactorySettings settings)
        {
            yield return $"{tab}[Table(\"{table.DbName}\")]";
            yield return $"{tab}public partial {(settings.UseRecords ? "record" : "class")} {table.DbName} : {(table.Type == TableType.Table ? "ITableModel" : "IViewModel")}";
            yield return tab + "{";

            foreach (var c in table.Columns.OrderByDescending(x => x.PrimaryKey).ThenBy(x => x.DbName))
            {
                if (c.PrimaryKey)
                    yield return $"{tab}{tab}[PrimaryKey]";

                foreach (var relationPart in c.RelationParts.Where(x => x.Type == RelationPartType.ForeignKey))
                {
                    yield return $"{tab}{tab}[ForeignKey(\"{relationPart.Relation.CandidateKey.Column.Table.DbName}\", \"{relationPart.Relation.CandidateKey.Column.DbName}\", \"{relationPart.Relation.Constraint}\")]";
                }

                if (c.AutoIncrement)
                    yield return $"{tab}{tab}[AutoIncrement]";

                if (c.Nullable)
                    yield return $"{tab}{tab}[Nullable]";

                if(c.Signed.HasValue && c.Length.HasValue)
                    yield return $"{tab}{tab}[Type(\"{c.DbType}\", {c.Length}, {(c.Signed.Value ? "true":"false")})]";
                else if(c.Signed.HasValue && !c.Length.HasValue)
                    yield return $"{tab}{tab}[Type(\"{c.DbType}\", {(c.Signed.Value ? "true" : "false")})]";
                else if (c.Length.HasValue)
                    yield return $"{tab}{tab}[Type(\"{c.DbType}\", {c.Length})]";
                else
                    yield return $"{tab}{tab}[Type(\"{c.DbType}\")]";

                yield return $"{tab}{tab}[Column(\"{c.DbName}\")]";
                yield return $"{tab}{tab}public virtual {c.ValueProperty.CsTypeName}{(c.ValueProperty.CsNullable || c.AutoIncrement ? "?" : "")} {c.DbName} {{ get; set; }}";
                yield return $"";

                foreach (var relationPart in c.RelationParts)
                {
                    var column = relationPart.Type == RelationPartType.ForeignKey
                        ? relationPart.Relation.CandidateKey.Column
                        : relationPart.Relation.ForeignKey.Column;

                    yield return $"{tab}{tab}[Relation(\"{column.Table.DbName}\", \"{column.DbName}\")]";

                    if (relationPart.Type == RelationPartType.ForeignKey)
                        yield return $"{tab}{tab}public virtual {column.Table.Model.CsTypeName} {column.Table.Model.CsTypeName} {{ get; }}";
                    else
                        yield return $"{tab}{tab}public virtual IEnumerable<{column.Table.Model.CsTypeName}> {column.Table.Model.CsTypeName} {{ get; }}";

                    yield return $"";
                }
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
            yield return "using System.Collections.Generic;";
            yield return "using DataLinq;";
            yield return "using DataLinq.Interfaces;";
            yield return "using DataLinq.Attributes;";
            yield return "";
            yield return $"namespace {namespaceName}";
            yield return "{";
        }
    }
}
