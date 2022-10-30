using DataLinq.Attributes;
using DataLinq.Extensions;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace DataLinq.Metadata
{
    public class FileFactoryOptions
    {
        public string NamespaceName { get; set; } = "Models";
        public string Tab { get; set; } = "    ";
        public bool UseRecords { get; set; } = true;
        public bool UseCache { get; set; } = true;
        public bool SeparateTablesAndViews { get; set; } = false;
        public List<string> Usings { get; set; } = new List<string> { "System", "DataLinq", "DataLinq.Interfaces", "DataLinq.Attributes" };
    }

    public class FileFactory
    {
        private readonly FileFactoryOptions options;

        public FileFactory(FileFactoryOptions options)
        {
            this.options = options;
        }

        public IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseMetadata database)
        {
            var dbCsTypeName = database.TableModels.Any(x => x.Model.CsTypeName == database.CsTypeName)
                ? $"{database.CsTypeName}Db"
                : database.CsTypeName;

            yield return ($"{dbCsTypeName}.cs",
                    FileHeader(options.NamespaceName, options.Usings)
                    .Concat(DatabaseFileContents(database, dbCsTypeName, options))
                    .Concat(FileFooter())
                    .ToJoinedString("\n"));

            foreach (var table in database.TableModels)
            {
                var usings = options.Usings.Concat(table.Model.ValueProperties
                        .Select(x => (x.CsType?.Namespace))
                        .Where(x => x != null))
                    .Concat(table.Model.RelationProperties
                        .Where(x => x.RelationPart.Type == RelationPartType.CandidateKey)
                        .Select(x => "System.Collections.Generic"))
                    .Distinct()
                    .Select(name => (name.StartsWith("System"), name))
                    .OrderByDescending(x => x.Item1)
                    .ThenBy(x => x.name)
                    .Select(x => x.name);

                var file =
                    FileHeader(options.NamespaceName, usings)
                    .Concat(ModelFileContents(table.Model, options))
                    .Concat(FileFooter())
                    .ToJoinedString("\n");

                var path = GetFilePath(table);

                yield return (path, file);
            }
        }

        private string GetFilePath(TableModelMetadata table)
        {
            var path = $"{table.Model.CsTypeName}.cs";

            if (options.SeparateTablesAndViews)
                return table.Table.Type == TableType.Table
                    ? $"Tables{Path.DirectorySeparatorChar}{path}"
                    : $"Views{Path.DirectorySeparatorChar}{path}";

            return path;
        }

        private IEnumerable<string> DatabaseFileContents(DatabaseMetadata database, string dbName, FileFactoryOptions settings)
        {
            var tab = settings.Tab;
            if (settings.UseCache)
                yield return $"{tab}[UseCache]";

            yield return $"{tab}[Database(\"{database.Name}\")]";
            yield return $"{tab}public interface {dbName} : IDatabaseModel";
            yield return tab + "{";

            foreach (var t in database.TableModels.OrderBy(x => x.Table.DbName))
            {
                yield return $"{tab}{tab}DbRead<{t.Model.CsTypeName}> {t.CsPropertyName} {{ get; }}";
            }

            yield return tab + "}";
        }

        private IEnumerable<string> ModelFileContents(ModelMetadata model, FileFactoryOptions settings)
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

        private IEnumerable<string> WriteEnum(EnumProperty property, string tab)
        {
            yield return $"{tab}public enum {property.CsTypeName}";
            yield return tab + "{";
            yield return $"{tab}{tab}Empty,";

            foreach (var val in property.EnumValues)
                yield return $"{tab}{tab}{val},";

            yield return tab + "}";
            yield return "";
        }

        private IEnumerable<string> FileFooter()
        {
            yield return "}";
        }

        private IEnumerable<string> FileHeader(string namespaceName, IEnumerable<string> usings)
        {
            foreach (var row in usings)
                yield return $"using {row};";

            yield return "";
            yield return $"namespace {namespaceName}";
            yield return "{";
        }
    }
}
