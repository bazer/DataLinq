using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Metadata;

public class GeneratorFileFactoryOptions
{
    public string? NamespaceName { get; set; } = null;
    public string Tab { get; set; } = "    ";
    public bool UseRecords { get; set; } = false;
    public bool UseFileScopedNamespaces { get; set; } = false;
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
                yield return $"{namespaceTab}{tab}public override {otherPart.ColumnIndex.Table.Model.CsTypeName} {relationProperty.CsName} => GetForeignKey<{otherPart.ColumnIndex.Table.Model.CsTypeName}>(nameof({relationProperty.CsName}));";
            }
            else
            {
                yield return $"{namespaceTab}{tab}public override IEnumerable<{otherPart.ColumnIndex.Table.Model.CsTypeName}> {relationProperty.CsName} => GetRelation<{otherPart.ColumnIndex.Table.Model.CsTypeName}>(nameof({relationProperty.CsName}));";
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

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Mutable{table.Model.CsTypeName}: Mutable<{table.Model.CsTypeName}>, IMutableInstance<{model.Database.CsTypeName}>";
        yield return namespaceTab + "{";

        yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsTypeName}(): base() {{}}";
        yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsTypeName}({table.Model.CsTypeName} immutable{table.Model.CsTypeName}): base(immutable{table.Model.CsTypeName}) {{}}";

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

        yield return $"{namespaceTab}public static class {model.CsTypeName}Extensions";
        yield return namespaceTab + "{";

        //Mutate
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsTypeName} Mutate(this {model.CsTypeName} model) => model is null";
        yield return $"{namespaceTab}{tab}{tab}? throw new ArgumentNullException(nameof(model))";
        yield return $"{namespaceTab}{tab}{tab}: new(model);";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsTypeName} Mutate(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}if (model is null)";
        yield return $"{namespaceTab}{tab}{tab}{tab}throw new ArgumentNullException(nameof(model));";
        yield return $"{namespaceTab}{tab}{tab}";
        yield return $"{namespaceTab}{tab}{tab}var mutable = model.Mutate();";
        yield return $"{namespaceTab}{tab}{tab}changes(mutable);";
        yield return $"{namespaceTab}{tab}{tab}return mutable;";
        yield return $"{namespaceTab}{tab}}}";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsTypeName} Mutate(this Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}changes(model);";
        yield return $"{namespaceTab}{tab}{tab}return model;";
        yield return $"{namespaceTab}{tab}}}";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsTypeName} MutateOrNew(this {model.CsTypeName} model) => model is null";
        yield return $"{namespaceTab}{tab}{tab}? new()";
        yield return $"{namespaceTab}{tab}{tab}: new(model);";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsTypeName} MutateOrNew(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) => model is null";
        yield return $"{namespaceTab}{tab}{tab}? new Mutable{model.CsTypeName}().Mutate(changes)";
        yield return $"{namespaceTab}{tab}{tab}: new Mutable{model.CsTypeName}(model).Mutate(changes);";

        //Insert
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Insert<T>(this Mutable{model.CsTypeName} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Insert(transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Insert(this Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Insert(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Insert<T>(this Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Insert(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Insert(this Transaction transaction, Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Insert(changes, transaction);";

        //Update
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.GetDataSource().Provider.Commit(transaction => model.Update(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Update(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update<T>(this Database<T> database, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Update(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update(this Transaction transaction, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Update<T>(this Mutable{model.CsTypeName} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Update(transaction));";
        
        //InsertOrUpdate
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.GetDataSource().Provider.Commit(transaction => model.InsertOrUpdate(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.InsertOrUpdate(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate<T>(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.InsertOrUpdate(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate(this Transaction transaction, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate<T>(this Mutable{model.CsTypeName} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.InsertOrUpdate(transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate(this Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.InsertOrUpdate(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate<T>(this Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.InsertOrUpdate(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} InsertOrUpdate(this Transaction transaction, Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";

        //Save
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save(this {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save<T>(this Database<T> database, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Update(model, changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save(this Transaction transaction, {model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save<T>(this Mutable{model.CsTypeName} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(database);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save(this Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save<T>(this Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, database);";
        yield return $"{namespaceTab}{tab}public static {model.CsTypeName} Save(this Transaction transaction, Mutable{model.CsTypeName} model, Action<Mutable{model.CsTypeName}> changes) =>";
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
