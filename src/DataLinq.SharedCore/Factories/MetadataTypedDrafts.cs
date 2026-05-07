using System;
using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public sealed record MetadataDatabaseDraft(string Name, CsTypeDeclaration CsType)
{
    public string? DbName { get; init; }
    public CsFileDeclaration? CsFile { get; init; }
    public SourceTextSpan? SourceSpan { get; init; }
    public IReadOnlyList<Attribute> Attributes { get; init; } = [];
    public IReadOnlyList<(Attribute Attribute, SourceTextSpan Span)> AttributeSourceSpans { get; init; } = [];
    public bool UseCache { get; init; }
    public IReadOnlyList<(CacheLimitType limitType, long amount)> CacheLimits { get; init; } = [];
    public IReadOnlyList<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; init; } = [];
    public IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; init; } = [];
    public IReadOnlyList<MetadataTableModelDraft> TableModels { get; init; } = [];
}

public sealed record MetadataTableModelDraft(
    string CsPropertyName,
    MetadataModelDraft Model,
    MetadataTableDraft Table)
{
    public bool IsStub { get; init; }
}

public sealed record MetadataModelDraft(CsTypeDeclaration CsType)
{
    public CsFileDeclaration? CsFile { get; init; }
    public SourceTextSpan? SourceSpan { get; init; }
    public CsTypeDeclaration? ImmutableType { get; init; }
    public Delegate? ImmutableFactory { get; init; }
    public CsTypeDeclaration? MutableType { get; init; }
    public CsTypeDeclaration? ModelInstanceInterface { get; init; }
    public IReadOnlyList<CsTypeDeclaration> OriginalInterfaces { get; init; } = [];
    public IReadOnlyList<ModelUsing> Usings { get; init; } = [];
    public IReadOnlyList<Attribute> Attributes { get; init; } = [];
    public IReadOnlyList<(Attribute Attribute, SourceTextSpan Span)> AttributeSourceSpans { get; init; } = [];
    public IReadOnlyList<MetadataValuePropertyDraft> ValueProperties { get; init; } = [];
    public IReadOnlyList<MetadataRelationPropertyDraft> RelationProperties { get; init; } = [];
}

public sealed record MetadataTableDraft(string DbName)
{
    public TableType Type { get; init; } = TableType.Table;
    public string? Definition { get; init; }
    public bool? UseCache { get; init; }
    public IReadOnlyList<(CacheLimitType limitType, long amount)> CacheLimits { get; init; } = [];
    public IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; init; } = [];
}

public sealed record MetadataColumnDraft(string DbName)
{
    public IReadOnlyList<DatabaseColumnType> DbTypes { get; init; } = [];
    public bool PrimaryKey { get; init; }
    public bool ForeignKey { get; init; }
    public bool AutoIncrement { get; init; }
    public bool Nullable { get; init; }
}

public sealed record MetadataValuePropertyDraft(
    string PropertyName,
    CsTypeDeclaration CsType,
    MetadataColumnDraft Column)
{
    public IReadOnlyList<Attribute> Attributes { get; init; } = [];
    public IReadOnlyList<(Attribute Attribute, SourceTextSpan Span)> AttributeSourceSpans { get; init; } = [];
    public PropertySourceInfo? SourceInfo { get; init; }
    public bool CsNullable { get; init; }
    public int? CsSize { get; init; }
    public EnumProperty? EnumProperty { get; init; }
}

public sealed record MetadataRelationPropertyDraft(
    string PropertyName,
    CsTypeDeclaration CsType)
{
    public IReadOnlyList<Attribute> Attributes { get; init; } = [];
    public IReadOnlyList<(Attribute Attribute, SourceTextSpan Span)> AttributeSourceSpans { get; init; } = [];
    public PropertySourceInfo? SourceInfo { get; init; }
    public bool CsNullable { get; init; }
    public string? RelationName { get; init; }
}

internal static class MetadataTypedDraftConverter
{
    public static Option<DatabaseDefinition, IDLOptionFailure> ToConstructionGraph(MetadataDatabaseDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Typed metadata draft cannot be null.");

        if (!ValidateTypedDraftShape(draft).TryUnwrap(out _, out var shapeFailure))
            return shapeFailure;

        var database = new DatabaseDefinition(draft.Name, draft.CsType, draft.DbName);
        if (draft.CsFile.HasValue)
            database.SetCsFileCore(draft.CsFile.Value);

        if (draft.SourceSpan.HasValue)
            database.SetSourceSpanCore(draft.SourceSpan.Value);

        database.SetAttributesCore(draft.Attributes ?? []);
        ApplyAttributeSourceSpans(draft.AttributeSourceSpans, database.SetAttributeSourceSpanCore);
        database.SetCacheCore(draft.UseCache);
        database.CacheLimits.AddRangeCore(draft.CacheLimits ?? []);
        database.CacheCleanup.AddRangeCore(draft.CacheCleanup ?? []);
        database.IndexCache.AddRangeCore(draft.IndexCache ?? []);

        var tableModels = new List<TableModel>();
        foreach (var tableModelDraft in draft.TableModels ?? [])
        {
            if (tableModelDraft is null)
                return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Typed metadata draft for database '{database.DbName}' contains a null table model.");

            var tableModelResult = CreateTableModel(database, tableModelDraft);
            if (!tableModelResult.TryUnwrap(out var tableModel, out var failure))
                return failure;

            tableModels.Add(tableModel);
        }

        database.SetTableModelsCore(tableModels);
        return database;
    }

    private static Option<bool, IDLOptionFailure> ValidateTypedDraftShape(MetadataDatabaseDraft draft)
    {
        var databaseName = draft.DbName ?? draft.Name;

        if (!ValidateAttributeSourceSpans(
                draft.AttributeSourceSpans,
                $"Typed database draft '{databaseName}'").TryUnwrap(out _, out var databaseAttributeSourceSpanFailure))
            return databaseAttributeSourceSpanFailure;

        foreach (var tableModelDraft in draft.TableModels ?? [])
        {
            if (tableModelDraft is null)
                return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Typed metadata draft for database '{databaseName}' contains a null table model draft.");

            var tableModelName = tableModelDraft.CsPropertyName;
            if (tableModelDraft.Model is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Typed table model draft '{tableModelName}' on database '{databaseName}' has no model draft.");

            if (tableModelDraft.Table is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"Typed table model draft '{tableModelName}' on database '{databaseName}' has no table draft.");

            if (!ValidateAttributeSourceSpans(
                    tableModelDraft.Model.AttributeSourceSpans,
                    $"Typed model draft '{tableModelDraft.Model.CsType.Name}' on database '{databaseName}'").TryUnwrap(out _, out var modelAttributeSourceSpanFailure))
                return modelAttributeSourceSpanFailure;

            foreach (var propertyDraft in tableModelDraft.Model.ValueProperties ?? [])
            {
                if (propertyDraft is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Typed table model draft '{tableModelName}' on database '{databaseName}' contains a null value property draft.");

                if (propertyDraft.Column is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Typed value property draft '{tableModelName}.{propertyDraft.PropertyName}' on database '{databaseName}' has no column draft.");

                if (!ValidateAttributeSourceSpans(
                        propertyDraft.AttributeSourceSpans,
                        $"Typed value property draft '{tableModelName}.{propertyDraft.PropertyName}' on database '{databaseName}'").TryUnwrap(out _, out var propertyAttributeSourceSpanFailure))
                    return propertyAttributeSourceSpanFailure;
            }

            foreach (var propertyDraft in tableModelDraft.Model.RelationProperties ?? [])
            {
                if (propertyDraft is null)
                    return DLOptionFailure.Fail(
                        DLFailureType.InvalidModel,
                        $"Typed table model draft '{tableModelName}' on database '{databaseName}' contains a null relation property draft.");

                if (!ValidateAttributeSourceSpans(
                        propertyDraft.AttributeSourceSpans,
                        $"Typed relation property draft '{tableModelName}.{propertyDraft.PropertyName}' on database '{databaseName}'").TryUnwrap(out _, out var propertyAttributeSourceSpanFailure))
                    return propertyAttributeSourceSpanFailure;
            }
        }

        return true;
    }

    private static Option<bool, IDLOptionFailure> ValidateAttributeSourceSpans(
        IEnumerable<(Attribute Attribute, SourceTextSpan Span)>? sourceSpans,
        string ownerDescription)
    {
        if (sourceSpans is null)
            return true;

        foreach (var (attribute, _) in sourceSpans)
        {
            if (attribute is null)
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    $"{ownerDescription} contains a null attribute source-span attribute.");
        }

        return true;
    }

    private static Option<TableModel, IDLOptionFailure> CreateTableModel(
        DatabaseDefinition database,
        MetadataTableModelDraft tableModelDraft)
    {
        if (tableModelDraft.Model is null)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Typed table model draft '{tableModelDraft.CsPropertyName}' on database '{database.DbName}' has no model draft.");

        if (tableModelDraft.Table is null)
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Typed table model draft '{tableModelDraft.CsPropertyName}' on database '{database.DbName}' has no table draft.");

        var model = CreateModel(tableModelDraft.Model);
        var tableResult = CreateTable(tableModelDraft.Table);
        if (!tableResult.TryUnwrap(out var table, out var failure))
            return failure;

        var tableModel = new TableModel(
            tableModelDraft.CsPropertyName,
            database,
            model,
            table,
            tableModelDraft.IsStub);

        PopulateModelProperties(model, table, tableModelDraft.Model);
        return tableModel;
    }

    private static ModelDefinition CreateModel(MetadataModelDraft draft)
    {
        var model = new ModelDefinition(draft.CsType);
        if (draft.CsFile.HasValue)
            model.SetCsFileCore(draft.CsFile.Value);

        if (draft.ImmutableType.HasValue)
            model.SetImmutableTypeCore(draft.ImmutableType.Value);

        if (draft.ImmutableFactory is not null)
            model.SetImmutableFactoryCore(draft.ImmutableFactory);

        if (draft.MutableType.HasValue)
            model.SetMutableTypeCore(draft.MutableType.Value);

        model.SetModelInstanceInterfaceCore(draft.ModelInstanceInterface);
        model.SetInterfacesCore(draft.OriginalInterfaces ?? []);
        model.SetUsingsCore(draft.Usings ?? []);
        model.SetAttributesCore(draft.Attributes ?? []);
        if (draft.SourceSpan.HasValue)
            model.SetSourceSpanCore(draft.SourceSpan.Value);

        ApplyAttributeSourceSpans(draft.AttributeSourceSpans, model.SetAttributeSourceSpanCore);

        return model;
    }

    private static Option<TableDefinition, IDLOptionFailure> CreateTable(MetadataTableDraft draft)
    {
        TableDefinition table;
        if (draft.Type == TableType.Table)
        {
            table = new TableDefinition(draft.DbName);
        }
        else if (draft.Type == TableType.View)
        {
            var view = new ViewDefinition(draft.DbName);
            if (draft.Definition is not null)
                view.SetDefinitionCore(draft.Definition);

            table = view;
        }
        else
        {
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Typed table draft '{draft.DbName}' uses unsupported table type '{draft.Type}'.");
        }

        if (draft.UseCache.HasValue)
            table.SetUseCacheCore(draft.UseCache.Value);

        table.CacheLimits.AddRangeCore(draft.CacheLimits ?? []);
        table.IndexCache.AddRangeCore(draft.IndexCache ?? []);

        return table;
    }

    private static void PopulateModelProperties(
        ModelDefinition model,
        TableDefinition table,
        MetadataModelDraft draft)
    {
        var columns = new List<ColumnDefinition>();
        foreach (var propertyDraft in draft.ValueProperties ?? [])
        {
            var property = CreateValueProperty(model, propertyDraft);
            model.AddPropertyCore(property);

            var column = CreateColumn(table, propertyDraft.Column);
            column.SetValuePropertyCore(property);
            ApplyColumnFlags(column, propertyDraft.Column);
            columns.Add(column);
        }

        table.SetColumnsCore(columns);

        foreach (var propertyDraft in draft.RelationProperties ?? [])
        {
            var property = new RelationProperty(
                propertyDraft.PropertyName,
                propertyDraft.CsType,
                model,
                propertyDraft.Attributes ?? []);

            property.SetCsNullableCore(propertyDraft.CsNullable);
            if (propertyDraft.SourceInfo.HasValue)
                property.SetSourceInfoCore(propertyDraft.SourceInfo.Value);

            if (propertyDraft.RelationName is not null)
                property.SetRelationNameCore(propertyDraft.RelationName);

            ApplyAttributeSourceSpans(propertyDraft.AttributeSourceSpans, property.SetAttributeSourceSpanCore);
            model.AddPropertyCore(property);
        }
    }

    private static ValueProperty CreateValueProperty(ModelDefinition model, MetadataValuePropertyDraft draft)
    {
        var property = new ValueProperty(
            draft.PropertyName,
            draft.CsType,
            model,
            draft.Attributes ?? []);

        property.SetCsNullableCore(draft.CsNullable);
        property.SetCsSizeCore(draft.CsSize);
        if (draft.SourceInfo.HasValue)
            property.SetSourceInfoCore(draft.SourceInfo.Value);

        if (draft.EnumProperty.HasValue)
            property.SetEnumPropertyCore(draft.EnumProperty.Value);

        ApplyAttributeSourceSpans(draft.AttributeSourceSpans, property.SetAttributeSourceSpanCore);
        return property;
    }

    private static ColumnDefinition CreateColumn(TableDefinition table, MetadataColumnDraft draft)
    {
        var column = new ColumnDefinition(draft.DbName, table);

        foreach (var dbType in draft.DbTypes ?? [])
            column.AddDbTypeCore(dbType is null ? null! : dbType.Clone());

        return column;
    }

    private static void ApplyColumnFlags(ColumnDefinition column, MetadataColumnDraft draft)
    {
        column.SetForeignKeyCore(draft.ForeignKey);
        column.SetAutoIncrementCore(draft.AutoIncrement);
        column.SetNullableCore(draft.Nullable);

        if (draft.PrimaryKey)
            column.SetPrimaryKeyCore();
    }

    private static void ApplyAttributeSourceSpans(
        IEnumerable<(Attribute Attribute, SourceTextSpan Span)>? sourceSpans,
        Action<Attribute, SourceTextSpan> setSourceSpan)
    {
        if (sourceSpans is null)
            return;

        foreach (var (attribute, sourceSpan) in sourceSpans)
            setSourceSpan(attribute, sourceSpan);
    }
}
