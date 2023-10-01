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
        public bool UseFileScopedNamespaces { get; set; }
        public bool SeparateTablesAndViews { get; set; } = false;
        public List<string> Usings { get; set; } = new List<string> { "System", /*"DataLinq",*/ "DataLinq.Interfaces", "DataLinq.Attributes" };
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
                    FileHeader(options.NamespaceName, options.UseFileScopedNamespaces, options.Usings)
                    .Concat(DatabaseFileContents(database, dbCsTypeName, options))
                    .Concat(FileFooter(options.UseFileScopedNamespaces))
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
                    .Where(name => name != options.NamespaceName)
                    .Select(name => (name.StartsWith("System"), name))
                    .OrderByDescending(x => x.Item1)
                    .ThenBy(x => x.name)
                    .Select(x => x.name);

                var file =
                    FileHeader(options.NamespaceName, options.UseFileScopedNamespaces, usings)
                    .Concat(ModelFileContents(table.Model, options))
                    .Concat(FileFooter(options.UseFileScopedNamespaces))
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
            var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
            var tab = settings.Tab;
            if (settings.UseCache)
                yield return $"{namespaceTab}[UseCache]";

            yield return $"{namespaceTab}[Database(\"{database.Name}\")]";
            yield return $"{namespaceTab}public interface {dbName} : IDatabaseModel";
            yield return namespaceTab + "{";

            foreach (var t in database.TableModels.OrderBy(x => x.Table.DbName))
            {
                yield return $"{namespaceTab}{tab}DbRead<{t.Model.CsTypeName}> {t.CsPropertyName} {{ get; }}";
            }

            yield return namespaceTab + "}";
        }

        private IEnumerable<string> ModelFileContents(ModelMetadata model, FileFactoryOptions options)
        {
            var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
            var tab = options.Tab;
            var table = model.Table;

            var props = model.Properties
                .OrderBy(x => x.Type)
                .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
                .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
                .ThenBy(x => x.CsName);

            foreach (var row in props.OfType<ValueProperty>().Where(x => x.EnumProperty != null && !x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(x, namespaceTab, tab)))
                yield return row;

            if (table is ViewMetadata view)
            {
                yield return $"{namespaceTab}[Definition(\"{view.Definition}\")]";
                yield return $"{namespaceTab}[View(\"{table.DbName}\")]";
            }
            else
            {
                yield return $"{namespaceTab}[Table(\"{table.DbName}\")]";
            }

            var interfaces = table.Type == TableType.Table ? "ITableModel" : "IViewModel";

            interfaces += $"<{model.Database.CsTypeName}>";
            //if (model.Interfaces?.Length > 0)
            //    interfaces += ", " + model.Interfaces.Select(x => x.Name).ToJoinedString(", ");

            yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} {table.Model.CsTypeName} : {interfaces}";
            yield return namespaceTab + "{";

            foreach (var row in props.OfType<ValueProperty>().Where(x => x.EnumProperty != null && x.EnumProperty.Value.DeclaredInClass).SelectMany(x => WriteEnum(x, namespaceTab, tab)))
                yield return tab + row;

            foreach (var property in props)
            {
                if (property is ValueProperty valueProperty)
                {
                    var c = valueProperty.Column;
                    if (c.PrimaryKey)
                        yield return $"{namespaceTab}{tab}[PrimaryKey]";

                    foreach (var index in table.ColumnIndices.Where(x => x.Type == IndexType.Unique && x.Columns.Contains(c)))
                    {
                        yield return $"{namespaceTab}{tab}[Unique(\"{index.ConstraintName}\")]";
                    }

                    foreach (var relationPart in c.RelationParts.Where(x => x.Type == RelationPartType.ForeignKey))
                    {
                        yield return $"{namespaceTab}{tab}[ForeignKey(\"{relationPart.Relation.CandidateKey.Column.Table.DbName}\", \"{relationPart.Relation.CandidateKey.Column.DbName}\", \"{relationPart.Relation.ConstraintName}\")]";
                    }

                    if (c.AutoIncrement)
                        yield return $"{namespaceTab}{tab}[AutoIncrement]";

                    if (c.Nullable)
                        yield return $"{namespaceTab}{tab}[Nullable]";

                    foreach (var dbType in c.DbTypes.OrderBy(x => x.DatabaseType))
                    {
                        if (dbType.Signed.HasValue && dbType.Length.HasValue)
                            yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length}, {(dbType.Signed.Value ? "true" : "false")})]";
                        else if (dbType.Signed.HasValue && !dbType.Length.HasValue)
                            yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {(dbType.Signed.Value ? "true" : "false")})]";
                        else if (dbType.Length.HasValue)
                            yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\", {dbType.Length})]";
                        else
                            yield return $"{namespaceTab}{tab}[Type(DatabaseType.{dbType.DatabaseType}, \"{dbType.Name}\")]";
                    }

                    if (valueProperty.EnumProperty != null)
                        yield return $"{namespaceTab}{tab}[Enum({string.Join(',', valueProperty.EnumProperty.Value.EnumValues.Select(x => $"\"{x.name}\""))})]";

                    yield return $"{namespaceTab}{tab}[Column(\"{c.DbName}\")]";
                    yield return $"{namespaceTab}{tab}public virtual {c.ValueProperty.CsTypeName}{(c.ValueProperty.CsNullable || c.AutoIncrement ? "?" : "")} {c.ValueProperty.CsName} {{ get; set; }}";
                    yield return $"";
                }
                else if (property is RelationProperty relationProperty)
                {
                    var otherPart = relationProperty.RelationPart.GetOtherSide();

                    yield return $"{namespaceTab}{tab}[Relation(\"{otherPart.Column.Table.DbName}\", \"{otherPart.Column.DbName}\")]";

                    if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
                        yield return $"{namespaceTab}{tab}public virtual {otherPart.Column.Table.Model.CsTypeName} {relationProperty.CsName} {{ get; }}";
                    else
                        yield return $"{namespaceTab}{tab}public virtual IEnumerable<{otherPart.Column.Table.Model.CsTypeName}> {relationProperty.CsName} {{ get; }}";

                    yield return $"";
                }
            }

            yield return namespaceTab + "}";
        }

        private IEnumerable<string> WriteEnum(ValueProperty property, string namespaceTab, string tab)
        {
            yield return $"{namespaceTab}public enum {property.CsTypeName}";
            yield return namespaceTab + "{";
            //yield return $"{tab}{tab}Empty,";

            var values = property.EnumProperty.Value.CsEnumValues.Count != 0
                ? property.EnumProperty.Value.CsEnumValues
                : property.EnumProperty.Value.EnumValues;

            foreach (var val in values)
                yield return $"{namespaceTab}{tab}{val.name} = {val.value},";

            yield return namespaceTab + "}";
            yield return "";
        }

        private IEnumerable<string> FileHeader(string namespaceName, bool useFileScopedNamespaces, IEnumerable<string> usings)
        {
            foreach (var row in usings)
                yield return $"using {row};";

            yield return "";
            yield return $"namespace {namespaceName}{(useFileScopedNamespaces ? ";" : "")}";


            if (useFileScopedNamespaces)
                yield return "";
            else
                yield return "{";
        }

        private IEnumerable<string> FileFooter(bool useFileScopedNamespaces)
        {
            if (!useFileScopedNamespaces)
                yield return "}";
        }
    }
}
