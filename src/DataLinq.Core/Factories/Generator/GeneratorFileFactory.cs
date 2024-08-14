using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Transactions;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Metadata;

public class GeneratorFileFactoryOptions
{
    public string NamespaceName { get; set; } = null; //"Models";
    public string Tab { get; set; } = "    ";
    public bool UseRecords { get; set; } = false;
    //public bool UseCache { get; set; } = true;
    public bool UseFileScopedNamespaces { get; set; }
    public bool SeparateTablesAndViews { get; set; } = false;
    public List<string> Usings { get; set; } = new List<string> { "System", "DataLinq", "DataLinq.Interfaces", "DataLinq.Attributes", "DataLinq.Mutation" };
}

public class GeneratorFileFactory
{
    private readonly GeneratorFileFactoryOptions options;

    public GeneratorFileFactory(GeneratorFileFactoryOptions options)
    {
        this.options = options;
        this.options.UseRecords = false;
    }

    public IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseMetadata database)
    {
        var dbCsTypeName = database.TableModels.Any(x => x.Model.CsTypeName == database.CsTypeName)
            ? $"{database.CsTypeName}Db"
            : database.CsTypeName;

        //yield return ($"{dbCsTypeName}.cs",
        //        FileHeader(options.NamespaceName ?? database.CsNamespace, options.UseFileScopedNamespaces, options.Usings)
        //        .Concat(DatabaseFileContents(database, dbCsTypeName, options))
        //        .Concat(FileFooter(options.UseFileScopedNamespaces))
        //        .ToJoinedString("\n"));

        foreach (var table in database.TableModels.Where(x => !x.IsStub))
        {
            var namespaceName = options.NamespaceName ?? table.Model.CsNamespace;
            if (namespaceName == null)
                throw new Exception($"Namespace is null for '{table.Model.CsTypeName}'");

            var usings = options.Usings
                .Concat(table.Model.Usings?.Select(x => x.FullNamespaceName) ?? new List<string>())
                .Concat(table.Model.RelationProperties.Values
                    .Where(x => x.RelationPart.Type == RelationPartType.CandidateKey)
                    .Select(x => "System.Collections.Generic"))
                .Distinct()
                .Where(x => x != null)
                .Where(name => name != namespaceName)
                .Select(name => (name.StartsWith("System"), name))
                .OrderByDescending(x => x.Item1)
                .ThenBy(x => x.name)
                .Select(x => x.name);

            var file =
                FileHeader(namespaceName, options.UseFileScopedNamespaces, usings)
                .Concat(ImmutableModelFileContents(table.Model, options))
                .Concat(MutableModelFileContents(table.Model, options))
                .Concat(ExtensionMethodsFileContents(table.Model, options))
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

    private IEnumerable<string> DatabaseFileContents(DatabaseMetadata database, string dbName, GeneratorFileFactoryOptions settings)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = settings.Tab;

        //if (database.UseCache)
        //    yield return $"{namespaceTab}[UseCache]";

        //foreach (var limit in database.CacheLimits)
        //    yield return $"{namespaceTab}[CacheLimit(CacheLimitType.{limit.limitType}, {limit.amount})]";

        //foreach (var cleanup in database.CacheCleanup)
        //    yield return $"{namespaceTab}[CacheCleanup(CacheCleanupType.{cleanup.cleanupType}, {cleanup.amount})]";

        //yield return $"{namespaceTab}[Database(\"{database.Name}\")]";
        yield return $"{namespaceTab}public partial class {dbName}(DataSourceAccess dataSource) : {database.CsInheritedInterfaceName}";
        yield return namespaceTab + "{";

        //yield return $"{namespaceTab}{tab}public ";

        foreach (var t in database.TableModels.OrderBy(x => x.Table.DbName))
        {
            yield return $"{namespaceTab}{tab}public DbRead<{t.Model.CsTypeName}> {t.CsPropertyName} {{ get; }} = new DbRead<{t.Model.CsTypeName}>(dataSource);";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ImmutableModelFileContents(ModelMetadata model, GeneratorFileFactoryOptions options)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;
        var table = model.Table;

        var valueProps = model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.CsName)
            .ToList();

        var relationProps = model.RelationProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.CsName)
            .ToList();

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Immutable{table.Model.CsTypeName}(RowData rowData, DataSourceAccess dataSource) : {table.Model.CsTypeName}(rowData, dataSource)";
        yield return namespaceTab + "{";

        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;
            
            yield return $"{namespaceTab}{tab}private {GetCsTypeName(c.ValueProperty)}{GetFieldNullable(c.ValueProperty)} _{c.ValueProperty.CsName};";
            yield return $"{namespaceTab}{tab}public override {GetCsTypeName(c.ValueProperty)}{GetPropertyNullable(c.ValueProperty)} {c.ValueProperty.CsName} => _{c.ValueProperty.CsName} ??= GetValue<{c.ValueProperty.CsTypeName}>(nameof({c.ValueProperty.CsName}));";
            yield return $"";
        }

        foreach (var relationProperty in relationProps)
        {
            var otherPart = relationProperty.RelationPart.GetOtherSide();

            if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
            {
                yield return $"{namespaceTab}{tab}private {otherPart.ColumnIndex.Table.Model.CsTypeName} _{relationProperty.CsName};";
                yield return $"{namespaceTab}{tab}public override {otherPart.ColumnIndex.Table.Model.CsTypeName} {relationProperty.CsName} => _{relationProperty.CsName} ??= GetForeignKey<{otherPart.ColumnIndex.Table.Model.CsTypeName}>(nameof({relationProperty.CsName}));";
            }
            else
            {
                yield return $"{namespaceTab}{tab}private IEnumerable<{otherPart.ColumnIndex.Table.Model.CsTypeName}> _{relationProperty.CsName};";
                yield return $"{namespaceTab}{tab}public override IEnumerable<{otherPart.ColumnIndex.Table.Model.CsTypeName}> {relationProperty.CsName} => _{relationProperty.CsName} ??= GetRelation<{otherPart.ColumnIndex.Table.Model.CsTypeName}>(nameof({relationProperty.CsName}));";
            }

            yield return $"";
        }

        yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsTypeName} Mutate() => new(this);";
        yield return namespaceTab + "}";
    }

    private IEnumerable<string> MutableModelFileContents(ModelMetadata model, GeneratorFileFactoryOptions options)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;
        var table = model.Table;

        var valueProps = model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.CsName)
            .ToList();

        var relationProps = model.RelationProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.CsName)
            .ToList();

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Mutable{table.Model.CsTypeName}: Mutable<{table.Model.CsTypeName}>";
        yield return namespaceTab + "{";

        yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsTypeName}(): base() {{}}";
        yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsTypeName}({table.Model.CsTypeName} immutable{table.Model.CsTypeName}): base(immutable{table.Model.CsTypeName}.GetRowData()) {{}}";

        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;

            yield return $"";
            yield return $"{namespaceTab}{tab}public virtual {GetCsTypeName(c.ValueProperty)}{GetPropertyNullable(c.ValueProperty)} {c.ValueProperty.CsName}";
            yield return $"{namespaceTab}{tab}{{";
            yield return $"{namespaceTab}{tab}{tab}get => GetValue<{GetCsTypeName(c.ValueProperty)}>(nameof({c.ValueProperty.CsName}));";
            yield return $"{namespaceTab}{tab}{tab}set => SetValue(nameof({c.ValueProperty.CsName}), value);";
            yield return $"{namespaceTab}{tab}}}";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ExtensionMethodsFileContents(ModelMetadata model, GeneratorFileFactoryOptions options)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;

        //public static class EmployeeExtensions
        //{
        //    public static MutableEmployee Mutate(this Employee model) => new(model);

        //    public static Employee Update(this Employee model, Action<MutableEmployee> changes, Transaction transaction)
        //    {
        //        var mutable = new MutableEmployee(model);
        //        changes(mutable);

        //        return transaction.Update(mutable);
        //    }

        //    public static Employee InsertOrUpdate(this Employee model, Action<MutableEmployee> changes, Transaction transaction)
        //    {
        //        var mutable = model == null
        //            ? new MutableEmployee()
        //            : new MutableEmployee(model);

        //        changes(mutable);

        //        return transaction.InsertOrUpdate(mutable);
        //    }

        //    public static Employee Update<T>(this Database<T> database, Employee model, Action<MutableEmployee> changes) where T : class, IDatabaseModel =>
        //        database.Commit(transaction => model.Update(changes, transaction));

        //    public static Employee InsertOrUpdate<T>(this Database<T> database, Employee model, Action<MutableEmployee> changes) where T : class, IDatabaseModel =>
        //        database.Commit(transaction => model.InsertOrUpdate(changes, transaction));

        //    public static Employee Update(this Transaction transaction, Employee model, Action<MutableEmployee> changes) =>
        //        model.Update(changes, transaction);

        //    public static Employee InsertOrUpdate(this Transaction transaction, Employee model, Action<MutableEmployee> changes) =>
        //        model.InsertOrUpdate(changes, transaction);
        //}

        yield return $"{namespaceTab}public static class {model.CsTypeName}Extensions";
        yield return namespaceTab + "{";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsTypeName} Mutate(this {model.CsTypeName} model) => new(model);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}var mutable = new Mutable{model.CsTypeName}(model);";
        yield return $"{namespaceTab}{tab}{tab}changes(mutable);";
        yield return $"{namespaceTab}{tab}{tab}return transaction.Update(mutable);";
        yield return $"{namespaceTab}{tab}}}";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}var mutable = model == null ? new Mutable{model.CsTypeName}() : new Mutable{model.CsTypeName}(model);";
        yield return $"{namespaceTab}{tab}{tab}changes(mutable);";
        yield return $"{namespaceTab}{tab}{tab}return transaction.InsertOrUpdate(mutable);";
        yield return $"{namespaceTab}{tab}}}";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update<T>(this Database<T> database, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Update(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate<T>(this Database<T> database, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.InsertOrUpdate(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update(this Transaction transaction, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate(this Transaction transaction, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";
        yield return namespaceTab + "}";
    }

    private string GetCsTypeName(ValueProperty property)
    {
        string name = string.Empty;

        if (property.EnumProperty?.DeclaredInClass == true)
            name += $"{property.Model.CsTypeName}.";

        name += property.CsTypeName;
        
        return name;
    }

    private string GetPropertyNullable(ValueProperty property)
    {
        return (property.CsNullable || property.Column.AutoIncrement) ? "?" : "";
    }

    private string GetFieldNullable(ValueProperty property)
    {
        return property.CsNullable || property.EnumProperty.HasValue || MetadataTypeConverter.IsCsTypeNullable(property.CsTypeName) || !MetadataTypeConverter.IsKnownCsType(property.CsTypeName) ? "?" : "";
    }

    private IEnumerable<string> WriteEnum(ValueProperty property, string namespaceTab, string tab)
    {
        yield return $"{namespaceTab}public enum {property.CsTypeName}";
        yield return namespaceTab + "{";
        //yield return $"{tab}{tab}Empty,";

        foreach (var val in property.EnumProperty.Value.EnumValues)
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
