using DataLinq.Attributes;
using DataLinq.Extensions;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace DataLinq.Metadata
{
    public class FileFactorySettings
    {
        public string NamespaceName { get; set; } = "Models";
        public string Tab { get; set; } = "    ";
        public bool UseRecords { get; set; } = true;
        public bool UseCache { get; set; } = true;
    }

    public static class FileFactory
    {
        //static readonly string tab = "    ";

        public static IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseMetadata database, FileFactorySettings settings)
        {
            var dbName = database.Tables.Any(x => x.Model.CsTypeName == database.Name)
                ? $"{database.Name}Db"
                : database.Name;

            yield return ($"{dbName}.cs",
                    FileHeader(settings.NamespaceName)
                    .Concat(DatabaseFileContents(database, dbName, settings))
                    .Concat(FileFooter())
                    .ToJoinedString("\n"));

            foreach (var table in database.Tables)
            {
                var file =
                    FileHeader(settings.NamespaceName)
                    .Concat(ModelFileContents(table.Model, settings))
                    .Concat(FileFooter())
                    .ToJoinedString("\n");

                var path = table.Type == TableType.Table
                    ? $"Tables{Path.DirectorySeparatorChar}{table.Model.CsTypeName}.cs"
                    : $"Views{Path.DirectorySeparatorChar}{table.Model.CsTypeName}.cs";

                yield return (path, file);
            }
        }

        private static IEnumerable<string> DatabaseFileContents(DatabaseMetadata database, string dbName, FileFactorySettings settings)
        {
            var tab = settings.Tab;
            if (settings.UseCache)
                yield return $"{tab}[UseCache]";

            yield return $"{tab}[Database(\"{database.Name}\")]";
            yield return $"{tab}public interface {dbName} : IDatabaseModel";
            yield return tab + "{";

            foreach (var t in database.Tables.OrderBy(x => x.DbName))
            {
                yield return $"{tab}{tab}DbRead<{t.Model.CsTypeName}> {t.Model.CsDatabasePropertyName} {{ get; }}";
            }

            yield return tab + "}";
        }

        private static IEnumerable<string> ModelFileContents(ModelMetadata model, FileFactorySettings settings)
        {
            var tab = settings.Tab;
            var table = model.Table;

            var props = model.Properties
                .OrderBy(x => x.Type)
                .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
                .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
                .ThenBy(x => x.CsName);

            foreach (var row in props.OfType<EnumProperty>().Where(x => !x.DeclaredInClass).SelectMany(x => WriteEnum(x, tab)))
                yield return row;

            if (table is ViewMetadata view)
            {
                yield return $"{tab}[Definition(\"{view.Definition}\")]";
                yield return $"{tab}[View(\"{table.DbName}\")]";
            }
            else
            {
                yield return $"{tab}[Table(\"{table.DbName}\")]";
            }

            var interfaces = table.Type == TableType.Table ? "ITableModel" : "IViewModel";
            //if (model.Interfaces?.Length > 0)
            //    interfaces += ", " + model.Interfaces.Select(x => x.Name).ToJoinedString(", ");

            yield return $"{tab}public partial {(settings.UseRecords ? "record" : "class")} {table.Model.CsTypeName} : {interfaces}";
            yield return tab + "{";

            foreach (var row in props.OfType<EnumProperty>().Where(x => x.DeclaredInClass).SelectMany(x => WriteEnum(x, tab)))
                yield return tab + row;

            foreach (var property in props)
            {
                if (property is ValueProperty valueProperty)
                {
                    var c = valueProperty.Column;
                    if (c.PrimaryKey)
                        yield return $"{tab}{tab}[PrimaryKey]";

                    foreach (var index in table.ColumnIndices.Where(x => x.Type == IndexType.Unique && x.Columns.Contains(c)))
                    {
                        yield return $"{tab}{tab}[Unique(\"{index.ConstraintName}\")]";
                    }

                    foreach (var relationPart in c.RelationParts.Where(x => x.Type == RelationPartType.ForeignKey))
                    {
                        yield return $"{tab}{tab}[ForeignKey(\"{relationPart.Relation.CandidateKey.Column.Table.DbName}\", \"{relationPart.Relation.CandidateKey.Column.DbName}\", \"{relationPart.Relation.ConstraintName}\")]";
                    }

                    if (c.AutoIncrement)
                        yield return $"{tab}{tab}[AutoIncrement]";

                    if (c.Nullable)
                        yield return $"{tab}{tab}[Nullable]";

                    foreach (var dbType in c.DbTypes)
                    {
                        if (dbType.Signed.HasValue && dbType.Length.HasValue)
                            yield return $"{tab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length}, {(dbType.Signed.Value ? "true" : "false")})]";
                        else if (dbType.Signed.HasValue && !dbType.Length.HasValue)
                            yield return $"{tab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {(dbType.Signed.Value ? "true" : "false")})]";
                        else if (dbType.Length.HasValue)
                            yield return $"{tab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length})]";
                        else
                            yield return $"{tab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\")]";
                    }

                    if (property is EnumProperty enumProperty)
                        yield return $"{tab}{tab}[Enum({string.Join(',', enumProperty.EnumValues.Select(x => $"\"{x}\""))})]";

                    yield return $"{tab}{tab}[Column(\"{c.DbName}\")]";
                    yield return $"{tab}{tab}public virtual {c.ValueProperty.CsTypeName}{(c.ValueProperty.CsNullable || c.AutoIncrement ? "?" : "")} {c.ValueProperty.CsName} {{ get; set; }}";
                    yield return $"";
                }
                else if (property is RelationProperty relationProperty)
                {
                    var otherPart = relationProperty.RelationPart.GetOtherSide();

                    yield return $"{tab}{tab}[Relation(\"{otherPart.Column.Table.DbName}\", \"{otherPart.Column.DbName}\")]";

                    if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
                        yield return $"{tab}{tab}public virtual {otherPart.Column.Table.Model.CsTypeName} {relationProperty.CsName} {{ get; }}";
                    else
                        yield return $"{tab}{tab}public virtual IEnumerable<{otherPart.Column.Table.Model.CsTypeName}> {relationProperty.CsName} {{ get; }}";

                    yield return $"";
                }
            }

            yield return tab + "}";
        }

        private static IEnumerable<string> WriteEnum(EnumProperty property, string tab)
        {
            yield return $"{tab}public enum {property.CsTypeName}";
            yield return tab + "{";
            yield return $"{tab}{tab}Empty,";

            foreach (var val in property.EnumValues)
                yield return $"{tab}{tab}{val},";

            yield return tab + "}";
            yield return "";
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
