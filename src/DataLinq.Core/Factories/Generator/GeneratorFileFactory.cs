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
    public bool UseNullableReferenceTypes { get; set; } = false;
    public bool SeparateTablesAndViews { get; set; } = false;
    public List<string> Usings { get; set; } = new List<string> { "System", "DataLinq", "DataLinq.Interfaces", "DataLinq.Attributes", "DataLinq.Mutation" };
}

public class GeneratorFileFactory
{
    public GeneratorFileFactoryOptions Options { get; }

    public GeneratorFileFactory(GeneratorFileFactoryOptions options)
    {
        this.Options = options;
        this.Options.UseRecords = false;
    }

    public IEnumerable<(string path, string contents)> CreateModelFiles(DatabaseDefinition database)
    {
        var dbCsTypeName = database.TableModels.Any(x => x.Model.CsType.Name == database.CsType.Name)
            ? $"{database.CsType.Name}Db"
            : database.CsType.Name;

        foreach (var table in database.TableModels.Where(x => !x.IsStub))
        {
            var namespaceName = Options.NamespaceName ?? table.Model.CsType.Namespace;
            if (namespaceName == null)
                throw new Exception($"Namespace is null for '{table.Model.CsType.Name}'");

            var usings = Options.Usings
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


            var parts = FileHeader(namespaceName, Options.UseFileScopedNamespaces, usings)
                    .Concat(ImmutableModelFileContents(table.Model, Options));

            if (table.Table.Type == TableType.Table)
            {
                parts = parts
                    .Concat(MutableModelFileContents(table.Model, Options))
                    .Concat(ExtensionMethodsFileContents(table.Model, Options));
            }

            var file = parts
                    .Concat(FileFooter(Options.UseFileScopedNamespaces))
                    .ToJoinedString("\n");

            var path = GetFilePath(table);

            yield return (path, file);
        }
    }

    private string GetFilePath(TableModel table)
    {
        var path = $"{table.Model.CsType.Name}.cs";

        if (Options.SeparateTablesAndViews)
            return table.Table.Type == TableType.Table
                ? $"Tables{Path.DirectorySeparatorChar}{path}"
                : $"Views{Path.DirectorySeparatorChar}{path}";

        return path;
    }

    private IEnumerable<string> ImmutableModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;
        var table = model.Table;

        var valueProps = model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .ToList();

        var relationProps = model.RelationProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .ToList();

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Immutable{table.Model.CsType.Name}(RowData rowData, DataSourceAccess dataSource) : {table.Model.CsType.Name}(rowData, dataSource)";
        yield return namespaceTab + "{";

        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;

            yield return $"{namespaceTab}{tab}private {GetCsTypeName(c.ValueProperty)}{GetFieldNullable(c.ValueProperty)} _{c.ValueProperty.PropertyName};";
            yield return $"{namespaceTab}{tab}public override {GetCsTypeName(c.ValueProperty)}{GetImmutablePropertyNullable(c.ValueProperty)} {c.ValueProperty.PropertyName} => _{c.ValueProperty.PropertyName} ??= {(IsImmutableGetterNullable(valueProperty) ? "GetNullableValue" : "GetValue")}<{c.ValueProperty.CsType.Name}>(nameof({c.ValueProperty.PropertyName}));";
            yield return $"";
        }

        foreach (var relationProperty in relationProps)
        {
            var otherPart = relationProperty.RelationPart.GetOtherSide();

            if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
            {
                yield return $"{namespaceTab}{tab}public override {otherPart.ColumnIndex.Table.Model.CsType.Name} {relationProperty.PropertyName} => GetForeignKey<{otherPart.ColumnIndex.Table.Model.CsType.Name}>(nameof({relationProperty.PropertyName}));";
            }
            else
            {
                yield return $"{namespaceTab}{tab}private ImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}>{GetUseNullableReferenceTypes()} _{relationProperty.PropertyName};";
                yield return $"{namespaceTab}{tab}public override ImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}> {relationProperty.PropertyName} => _{relationProperty.PropertyName} ??= GetImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}>(nameof({relationProperty.PropertyName}));";
            }

            yield return $"";
        }

        if (table.Type == TableType.Table)
            yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsType.Name} Mutate() => new(this);";

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> MutableModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;
        var table = model.Table;

        var valueProps = model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .ToList();

        var relationProps = model.RelationProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(x => x is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(x => x is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .ToList();

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Mutable{table.Model.CsType.Name}: Mutable<{table.Model.CsType.Name}>, IMutableInstance<{model.Database.CsType.Name}>";
        yield return namespaceTab + "{";

        yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsType.Name}(): base() {{}}";
        yield return $"{namespaceTab}{tab}public Mutable{table.Model.CsType.Name}({table.Model.CsType.Name} immutable{table.Model.CsType.Name}): base(immutable{table.Model.CsType.Name}) {{}}";

        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;

            yield return $"";
            yield return $"{namespaceTab}{tab}public virtual {GetCsTypeName(c.ValueProperty)}{GetMutablePropertyNullable(c.ValueProperty)} {c.ValueProperty.PropertyName}";
            yield return $"{namespaceTab}{tab}{{";
            yield return $"{namespaceTab}{tab}{tab}get => GetValue<{GetCsTypeName(c.ValueProperty)}>(nameof({c.ValueProperty.PropertyName}));";
            yield return $"{namespaceTab}{tab}{tab}set => SetValue(nameof({c.ValueProperty.PropertyName}), value);";
            yield return $"{namespaceTab}{tab}}}";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ExtensionMethodsFileContents(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
        var namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        var tab = options.Tab;

        yield return $"{namespaceTab}public static class {model.CsType.Name}Extensions";
        yield return namespaceTab + "{";

        //Mutate
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(this {model.CsType.Name} model) => model is null";
        yield return $"{namespaceTab}{tab}{tab}? throw new ArgumentNullException(nameof(model))";
        yield return $"{namespaceTab}{tab}{tab}: new(model);";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}if (model is null)";
        yield return $"{namespaceTab}{tab}{tab}{tab}throw new ArgumentNullException(nameof(model));";
        yield return $"{namespaceTab}{tab}{tab}";
        yield return $"{namespaceTab}{tab}{tab}var mutable = model.Mutate();";
        yield return $"{namespaceTab}{tab}{tab}changes(mutable);";
        yield return $"{namespaceTab}{tab}{tab}return mutable;";
        yield return $"{namespaceTab}{tab}}}";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes)";
        yield return $"{namespaceTab}{tab}{{";
        yield return $"{namespaceTab}{tab}{tab}changes(model);";
        yield return $"{namespaceTab}{tab}{tab}return model;";
        yield return $"{namespaceTab}{tab}}}";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name} model) => model is null";
        yield return $"{namespaceTab}{tab}{tab}? new()";
        yield return $"{namespaceTab}{tab}{tab}: new(model);";
        yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) => model is null";
        yield return $"{namespaceTab}{tab}{tab}? new Mutable{model.CsType.Name}().Mutate(changes)";
        yield return $"{namespaceTab}{tab}{tab}: new Mutable{model.CsType.Name}(model).Mutate(changes);";

        //Insert
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Insert(transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Insert(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert<T>(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Insert(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Insert(this Transaction transaction, Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Insert(changes, transaction);";

        //Update
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.GetDataSource().Provider.Commit(transaction => model.Update(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Update(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update<T>(this Database<T> database, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Update(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update(this Transaction transaction, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Update<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Update(transaction));";

        //InsertOrUpdate
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.GetDataSource().Provider.Commit(transaction => model.InsertOrUpdate(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.InsertOrUpdate(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate<T>(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.InsertOrUpdate(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate(this Transaction transaction, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.InsertOrUpdate(transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.InsertOrUpdate(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate<T>(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.InsertOrUpdate(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} InsertOrUpdate(this Transaction transaction, Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";

        //Save
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Database<T> database, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Update(model, changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Transaction transaction, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(database);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, database);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Transaction transaction, Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.InsertOrUpdate(changes, transaction);";

        yield return namespaceTab + "}";
    }

    private string GetCsTypeName(ValueProperty property)
    {
        string name = string.Empty;

        if (property.EnumProperty?.DeclaredInClass == true)
            name += $"{property.Model.CsType.Name}.";

        name += property.CsType.Name;

        return name;
    }

    private string GetImmutablePropertyNullable(ValueProperty property)
    {
        return IsPropertyNullable(property) ? "?" : "";
    }

    private string GetMutablePropertyNullable(ValueProperty property)
    {
        return Options.UseNullableReferenceTypes || IsPropertyNullable(property) ? "?" : "";
    }

    private string GetFieldNullable(ValueProperty property)
    {
        return IsFieldNullable(property) ? "?" : "";
    }

    private string GetUseNullableReferenceTypes()
    {
        return Options.UseNullableReferenceTypes ? "?" : "";
    }

    private bool IsPropertyNullable(ValueProperty property)
    {
        return property.CsNullable || property.Column.AutoIncrement;
    }

    private bool IsImmutableGetterNullable(ValueProperty property)
    {
        return Options.UseNullableReferenceTypes ? IsPropertyNullable(property) : true;
    }

    private bool IsFieldNullable(ValueProperty property)
    {
        return Options.UseNullableReferenceTypes
            || property.CsNullable
            || property.EnumProperty.HasValue
            || MetadataTypeConverter.IsCsTypeNullable(property.CsType.Name)
            || !MetadataTypeConverter.IsKnownCsType(property.CsType.Name);
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
