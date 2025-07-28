using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Extensions.Helpers;
using Microsoft.CodeAnalysis;

namespace DataLinq.Metadata;

public class GeneratorFileFactoryOptions
{
    public string? NamespaceName { get; set; } = null;
    public string Tab { get; set; } = "    ";
    public bool UseRecords { get; set; } = false;
    public bool UseFileScopedNamespaces { get; set; } = false;
    public bool UseNullableReferenceTypes { get; set; } = false;
    public bool SeparateTablesAndViews { get; set; } = false;
    public List<string> Usings { get; set; } = new List<string> { "System", "System.Diagnostics.CodeAnalysis", "DataLinq", "DataLinq.Interfaces", "DataLinq.Attributes", "DataLinq.Mutation" };
}

public class GeneratorFileFactory
{
    private string namespaceTab;
    private string tab;

    public GeneratorFileFactoryOptions Options { get; }

    public GeneratorFileFactory(GeneratorFileFactoryOptions options)
    {
        this.Options = options;
        this.Options.UseRecords = false;

        namespaceTab = options.UseFileScopedNamespaces ? "" : options.Tab;
        tab = options.Tab;
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


            var file =
                FileHeader(namespaceName, Options.UseFileScopedNamespaces, usings)
                .Concat(ModelFileContents(table.Model, Options))
                .Concat(FileFooter(Options.UseFileScopedNamespaces))
                .ToJoinedString("\n");

            var path = GetFilePath(table);

            yield return (path, file);
        }
    }

    private IEnumerable<string> ModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
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

        if (model.ModelInstanceInterface != null)
            foreach (var row in WriteInterface(model, model.ModelInstanceInterface.Value, options, valueProps))
                yield return row;

        foreach (var row in WriteBaseClassPartial(model, options))
            yield return row;

        foreach (var row in ImmutableModelFileContents(model, Options, valueProps, relationProps))
            yield return row;

        if (model.Table.Type == TableType.Table)
        {
            foreach (var row in MutableModelFileContents(model, Options, valueProps, relationProps))
                yield return row;

            foreach (var row in ExtensionMethodsFileContents(model, Options))
                yield return row;
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

    private IEnumerable<string> WriteInterface(ModelDefinition model, CsTypeDeclaration modelInterface, GeneratorFileFactoryOptions options, List<ValueProperty> valueProps)
    {
        yield return $"{namespaceTab}public partial interface {modelInterface.Name}: IModelInstance<{model.Database.CsType.Name}>";
        yield return namespaceTab + "{";

        foreach (var valueProperty in valueProps)
        {
            var prefix = valueProperty.EnumProperty != null && valueProperty.EnumProperty.Value.DeclaredInClass
                ? $"{model.CsType.Name}."
                : "";

            yield return $"{namespaceTab}{tab}{prefix}{valueProperty.CsType.Name}{GetInterfacePropertyNullable(valueProperty)} {valueProperty.PropertyName} {{ get; }}";
        }

        if (model.Table.Type == TableType.Table)
        {
            yield return "";
            yield return $"{namespaceTab}{tab}Mutable{model.CsType.Name} Mutate() => this switch";
            yield return $"{namespaceTab}{tab}{{";
            yield return $"{namespaceTab}{tab}{tab}Mutable{model.CsType.Name} mutable => mutable,";
            yield return $"{namespaceTab}{tab}{tab}Immutable{model.CsType.Name} immutable => immutable.Mutate(),";
            yield return $"{namespaceTab}{tab}{tab}_ => throw new NotSupportedException($\"Call to 'Mutate' not supported for type '{{GetType()}}'\")";
            yield return $"{namespaceTab}{tab}}};";
            yield return "";
            yield return $"{namespaceTab}{tab}Mutable{model.CsType.Name} Mutate(Action<Mutable{model.CsType.Name}> changes) => this switch";
            yield return $"{namespaceTab}{tab}{{";
            yield return $"{namespaceTab}{tab}{tab}Mutable{model.CsType.Name} mutable => mutable.Mutate(changes),";
            yield return $"{namespaceTab}{tab}{tab}Immutable{model.CsType.Name} immutable => immutable.Mutate(changes),";
            yield return $"{namespaceTab}{tab}{tab}_ => throw new NotSupportedException($\"Call to 'Mutate' not supported for type '{{GetType()}}'\")";
            yield return $"{namespaceTab}{tab}}};";
        }

        yield return namespaceTab + "}";
        yield return "";
    }

    private IEnumerable<string> WriteBaseClassPartial(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
        yield return $"{namespaceTab}public abstract partial {(options.UseRecords ? "record" : "class")} {model.CsType.Name}{(model.ModelInstanceInterface != null ? $": {model.ModelInstanceInterface.Value.Name}" : "")}";
        yield return namespaceTab + "{";

        if (model.Table.Type == TableType.Table)
        {
            if (model.Table.PrimaryKeyColumns.Length > 0)
            {
                var primaryKeys = model.Table.PrimaryKeyColumns
                    .Select(c => c.ValueProperty)
                    .ToList();

                var keyString = primaryKeys
                    .Select(x => $"{x.CsType.Name} {x.PropertyName.ToCamelCase()}")
                    .ToJoinedString(", ");

                var keyValues = primaryKeys
                    .Select(x => $"{x.PropertyName.ToCamelCase()}")
                    .ToJoinedString(", ");

                if (primaryKeys.Count == 1)
                {
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, DataSourceAccess dataSource) => IImmutable<{model.CsType.Name}>.Get(KeyFactory.CreateKeyFromValue({keyValues}), dataSource);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Database<{model.Database.CsType.Name}> database) => IImmutable<{model.CsType.Name}>.Get(KeyFactory.CreateKeyFromValue({keyValues}), database.Provider.ReadOnlyAccess);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Transaction<{model.Database.CsType.Name}> transaction) => IImmutable<{model.CsType.Name}>.Get(KeyFactory.CreateKeyFromValue({keyValues}), transaction);";
                }
                else
                {
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, DataSourceAccess dataSource) => IImmutable<{model.CsType.Name}>.Get(KeyFactory.CreateKeyFromValues([{keyValues}]), dataSource);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Database<{model.Database.CsType.Name}> database) => IImmutable<{model.CsType.Name}>.Get(KeyFactory.CreateKeyFromValues([{keyValues}]), database.Provider.ReadOnlyAccess);";
                    yield return $"{namespaceTab}{tab}public static {model.CsType.Name}{GetUseNullableReferenceTypes()} Get({keyString}, Transaction<{model.Database.CsType.Name}> transaction) => IImmutable<{model.CsType.Name}>.Get(KeyFactory.CreateKeyFromValues([{keyValues}]), transaction);";
                }

                yield return $"";
            }


            var requiredProps = GetRequiredValueProperties(model);

            if (requiredProps.Any())
            {
                var constructorParams = requiredProps.Select(GetConstructorParam).ToJoinedString(", ");
                var constructorArgs = requiredProps.Select(v => v.Column.ValueProperty.PropertyName.ToCamelCase()).ToJoinedString(", ");

                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({constructorParams}) => new({constructorArgs});";
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({constructorParams}, Action<Mutable{model.CsType.Name}> changes) => new Mutable{model.CsType.Name}({constructorArgs}).Mutate(changes);";
            }
            else
            {
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate() => new();";
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate(Action<Mutable{model.CsType.Name}> changes) => new Mutable{model.CsType.Name}().Mutate(changes);";
            }

            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.CsType.Name} model) => new Mutable{model.CsType.Name}(model);";
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) => new Mutable{model.CsType.Name}(model).Mutate(changes);";

            if (model.ModelInstanceInterface != null)
            {
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.ModelInstanceInterface.Value.Name} model) => model.Mutate();";
                yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} Mutate({model.ModelInstanceInterface.Value.Name} model, Action<Mutable{model.CsType.Name}> changes) => model.Mutate(changes);";
            }
        }

        yield return namespaceTab + "}";
        yield return "";
    }

    private IEnumerable<string> ImmutableModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options, List<ValueProperty> valueProps, List<RelationProperty> relationProps)
    {
        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Immutable{model.CsType.Name}(IRowData rowData, IDataSourceAccess dataSource) : {model.CsType.Name}(rowData, dataSource)";
        yield return namespaceTab + "{";

        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;

            yield return $"{namespaceTab}{tab}private {GetCsTypeName(c.ValueProperty)}{GetImmutableFieldNullable(c.ValueProperty)} _{c.ValueProperty.PropertyName};";
            yield return $"{namespaceTab}{tab}public override {GetCsTypeName(c.ValueProperty)}{GetImmutablePropertyNullable(c.ValueProperty)} {c.ValueProperty.PropertyName} => _{c.ValueProperty.PropertyName} ??= ({GetCsTypeName(c.ValueProperty)}{GetImmutablePropertyNullable(c.ValueProperty)}){(IsImmutableGetterNullable(valueProperty) ? "GetNullableValue" : "GetValue")}(nameof({c.ValueProperty.PropertyName}));";
            yield return $"";
        }

        foreach (var relationProperty in relationProps)
        {
            var otherPart = relationProperty.RelationPart.GetOtherSide();

            if (relationProperty.RelationPart.Type == RelationPartType.ForeignKey)
            {
                yield return $"{namespaceTab}{tab}private ImmutableForeignKey<{otherPart.ColumnIndex.Table.Model.CsType.Name}>{GetUseNullableReferenceTypes()} _{relationProperty.PropertyName};";
                yield return $"{namespaceTab}{tab}public override {otherPart.ColumnIndex.Table.Model.CsType.Name} {relationProperty.PropertyName} => _{relationProperty.PropertyName} ??= GetImmutableForeignKey<{otherPart.ColumnIndex.Table.Model.CsType.Name}>(nameof({relationProperty.PropertyName}));";
            }
            else
            {
                yield return $"{namespaceTab}{tab}private IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}>{GetUseNullableReferenceTypes()} _{relationProperty.PropertyName};";
                yield return $"{namespaceTab}{tab}public override IImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}> {relationProperty.PropertyName} => _{relationProperty.PropertyName} ??= GetImmutableRelation<{otherPart.ColumnIndex.Table.Model.CsType.Name}>(nameof({relationProperty.PropertyName}));";
            }

            yield return $"";
        }

        //if (model.Table.Type == TableType.Table)
        //    yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name} Mutate() => new(this);";

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> MutableModelFileContents(ModelDefinition model, GeneratorFileFactoryOptions options, List<ValueProperty> valueProps, List<RelationProperty> relationProps)
    {
        List<string> interfaces = [$"IMutableInstance<{model.Database.CsType.Name}>"];

        if (model.ModelInstanceInterface != null)
            interfaces.Add(model.ModelInstanceInterface.Value.Name);

        yield return $"{namespaceTab}public partial {(options.UseRecords ? "record" : "class")} Mutable{model.CsType.Name} : Mutable<{model.CsType.Name}>, {interfaces.ToJoinedString(", ")}";
        yield return namespaceTab + "{";

        var defaultProps = GetDefaultValueProperties(model);

        // Parameterless constructor for users who prefer setting properties via setters.
        yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name}() : base()";
        yield return $"{namespaceTab}{tab}" + "{";

        foreach (var v in defaultProps)
            yield return $"{namespaceTab}{tab}{tab}this.{v.PropertyName} = {v.GetDefaultValue()};";

        yield return $"{namespaceTab}{tab}" + "}";

        // Constructor with required properties.
        var requiredProps = GetRequiredValueProperties(model);
        if (requiredProps.Any())
        {
            var paramList = requiredProps.Select(GetConstructorParam).ToJoinedString(", ");

            // Decorate this constructor with the SetsRequiredMembers attribute.
            yield return $"";
            yield return $"{namespaceTab}{tab}[SetsRequiredMembers]";
            yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name}({paramList}) : this()";
            yield return $"{namespaceTab}{tab}" + "{";

            foreach (var v in defaultProps)
                yield return $"{namespaceTab}{tab}{tab}this.{v.PropertyName} = {v.GetDefaultValue()};";

            // For each required property, assign the passed parameter to the property.
            foreach (var v in requiredProps)
                yield return $"{namespaceTab}{tab}{tab}this.{v.PropertyName} = {v.PropertyName.ToCamelCase()};";

            yield return $"{namespaceTab}{tab}" + "}";
        }

        // Constructor that accepts an immutable instance.
        yield return $"";
        yield return $"{namespaceTab}{tab}[SetsRequiredMembers]";
        yield return $"{namespaceTab}{tab}public Mutable{model.CsType.Name}({model.CsType.Name} immutable{model.CsType.Name}) : base(immutable{model.CsType.Name}) {{}}";

        // Generate the properties as before.
        foreach (var valueProperty in valueProps)
        {
            var c = valueProperty.Column;
            yield return "";
            yield return $"{namespaceTab}{tab}public virtual {GetMutablePropertyRequired(c.ValueProperty)}{GetCsTypeName(c.ValueProperty)}{GetMutablePropertyNullable(c.ValueProperty)} {c.ValueProperty.PropertyName}";
            yield return $"{namespaceTab}{tab}" + "{";
            yield return $"{namespaceTab}{tab}{tab}get => ({GetCsTypeName(c.ValueProperty)}{GetMutablePropertyNullable(c.ValueProperty)})GetValue(nameof({c.ValueProperty.PropertyName}));";
            yield return $"{namespaceTab}{tab}{tab}set => SetValue(nameof({c.ValueProperty.PropertyName}), value);";
            yield return $"{namespaceTab}{tab}" + "}";
        }

        yield return namespaceTab + "}";
    }

    private IEnumerable<string> ExtensionMethodsFileContents(ModelDefinition model, GeneratorFileFactoryOptions options)
    {
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

        // First, compute the required constructor parameters and argument list.
        var requiredProps = GetRequiredValueProperties(model);

        // MutateOrNew
        if (requiredProps.Any())
        {
            var constructorParams = requiredProps.Select(GetConstructorParam).ToJoinedString(", ");
            var constructorArgs = requiredProps.Select(v => v.Column.ValueProperty.PropertyName.ToCamelCase()).ToJoinedString(", ");

            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model, {constructorParams}) => model is null ? new Mutable{model.CsType.Name}({constructorArgs}) : model.Mutate(x =>";
            yield return $"{namespaceTab}{tab}{{";
            foreach (var v in requiredProps)
                yield return $"{namespaceTab}{tab}{tab}x.{v.PropertyName} = {v.Column.ValueProperty.PropertyName.ToCamelCase()};";
            yield return $"{namespaceTab}{tab}}});";
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model, {constructorParams}, Action<Mutable{model.CsType.Name}> changes) => model.MutateOrNew({constructorArgs}).Mutate(changes);";
        }
        else
        {
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model) => model is null ? new() : new(model);";
            yield return $"{namespaceTab}{tab}public static Mutable{model.CsType.Name} MutateOrNew(this {model.CsType.Name}{GetUseNullableReferenceTypes()} model, Action<Mutable{model.CsType.Name}> changes) => model is null ? new Mutable{model.CsType.Name}().Mutate(changes) : new Mutable{model.CsType.Name}(model).Mutate(changes);";
        }

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

        //Save
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Database<T> database, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Update(model, changes);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Transaction transaction, {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Update(changes, transaction);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this {model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Save(changes, transaction));";

        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Mutable{model.CsType.Name} model, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Save(transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Save(model.Mutate(changes));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save<T>(this Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes, Database<T> database) where T : class, IDatabaseModel =>";
        yield return $"{namespaceTab}{tab}{tab}database.Commit(transaction => model.Save(changes, transaction));";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Mutable{model.CsType.Name} model, Transaction transaction) =>";
        yield return $"{namespaceTab}{tab}{tab}transaction.Save(model);";
        yield return $"{namespaceTab}{tab}public static {model.CsType.Name} Save(this Transaction transaction, Mutable{model.CsType.Name} model, Action<Mutable{model.CsType.Name}> changes) =>";
        yield return $"{namespaceTab}{tab}{tab}model.Save(changes, transaction);";

        yield return namespaceTab + "}";
    }

    private string GetConstructorParam(ValueProperty property)
    {
        var typeName = GetCsTypeName(property);
        var nullable = GetMutablePropertyNullable(property);
        var paramName = property.PropertyName.ToCamelCase();

        return $"{typeName}{nullable} {paramName}";
    }

    private List<ValueProperty> GetRequiredValueProperties(ModelDefinition model)
    {
        // Gather the required value properties for the mutable constructor.
        return model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(a => a is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(a => a is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .Where(v => IsMutablePropertyRequired(v.Column.ValueProperty))
            .ToList();
    }

    private List<ValueProperty> GetDefaultValueProperties(ModelDefinition model)
    {
        // Gather the required value properties for the mutable constructor.
        return model.ValueProperties.Values
            .OrderBy(x => x.Type)
            .ThenByDescending(x => x.Attributes.Any(a => a is PrimaryKeyAttribute))
            .ThenByDescending(x => x.Attributes.Any(a => a is ForeignKeyAttribute))
            .ThenBy(x => x.PropertyName)
            .Where(x => x.Column.ValueProperty.Attributes.Any(a => a is DefaultAttribute))
            .ToList();
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
        return IsImmutablePropertyNullable(property) ? "?" : "";
    }

    private string GetMutablePropertyNullable(ValueProperty property)
    {
        return IsInterfacePropertyNullable(property) ? "?" : "";
    }

    private string GetMutablePropertyRequired(ValueProperty property)
    {
        return IsMutablePropertyRequired(property) ? "required " : "";
    }

    private string GetImmutableFieldNullable(ValueProperty property)
    {
        return IsImmutableFieldNullable(property) ? "?" : "";
    }

    private string GetUseNullableReferenceTypes()
    {
        return Options.UseNullableReferenceTypes ? "?" : "";
    }

    private string GetInterfacePropertyNullable(ValueProperty property)
    {
        return IsInterfacePropertyNullable(property) ? "?" : "";
    }

    private bool IsInterfacePropertyNullable(ValueProperty property)
    {
        return (Options.UseNullableReferenceTypes || property.CsNullable) &&
            (property.Column.Nullable || property.Column.AutoIncrement);
    }

    private bool IsMutablePropertyRequired(ValueProperty property)
    {
        return !property.CsNullable &&
               !property.Column.Nullable &&
               !property.Column.AutoIncrement &&
               !property.Column.ForeignKey &&
               !property.HasDefaultValue();
    }

    private bool IsImmutablePropertyNullable(ValueProperty property)
    {
        return property.CsNullable || property.Column.AutoIncrement;
    }

    private bool IsImmutableGetterNullable(ValueProperty property)
    {
        return !Options.UseNullableReferenceTypes || IsImmutablePropertyNullable(property);
    }

    private bool IsImmutableFieldNullable(ValueProperty property)
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