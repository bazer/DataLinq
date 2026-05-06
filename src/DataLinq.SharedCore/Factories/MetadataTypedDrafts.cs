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
    public static Option<DatabaseDefinition, IDLOptionFailure> ToMutableMetadata(MetadataDatabaseDraft draft)
    {
        if (draft is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Typed metadata draft cannot be null.");

        var database = new DatabaseDefinition(draft.Name, draft.CsType, draft.DbName);
        if (draft.CsFile.HasValue)
            database.SetCsFile(draft.CsFile.Value);

        if (draft.SourceSpan.HasValue)
            database.SetSourceSpan(draft.SourceSpan.Value);

        database.SetAttributes(draft.Attributes ?? []);
        ApplyAttributeSourceSpans(draft.AttributeSourceSpans, database.SetAttributeSourceSpan);
        database.SetCache(draft.UseCache);
        database.CacheLimits.AddRange(draft.CacheLimits ?? []);
        database.CacheCleanup.AddRange(draft.CacheCleanup ?? []);
        database.IndexCache.AddRange(draft.IndexCache ?? []);

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

        database.SetTableModels(tableModels);
        return database;
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
            model.SetCsFile(draft.CsFile.Value);

        if (draft.ImmutableType.HasValue)
            model.SetImmutableType(draft.ImmutableType.Value);

        if (draft.ImmutableFactory is not null)
            model.SetImmutableFactory(draft.ImmutableFactory);

        if (draft.MutableType.HasValue)
            model.SetMutableType(draft.MutableType.Value);

        model.SetModelInstanceInterface(draft.ModelInstanceInterface);
        model.SetInterfaces(draft.OriginalInterfaces ?? []);
        model.SetUsings(draft.Usings ?? []);
        model.SetAttributes(draft.Attributes ?? []);
        if (draft.SourceSpan.HasValue)
            model.SetSourceSpan(draft.SourceSpan.Value);

        ApplyAttributeSourceSpans(draft.AttributeSourceSpans, model.SetAttributeSourceSpan);

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
                view.SetDefinition(draft.Definition);

            table = view;
        }
        else
        {
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Typed table draft '{draft.DbName}' uses unsupported table type '{draft.Type}'.");
        }

        if (draft.UseCache.HasValue)
            table.UseCache = draft.UseCache.Value;

        table.CacheLimits.AddRange(draft.CacheLimits ?? []);
        table.IndexCache.AddRange(draft.IndexCache ?? []);

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
            model.AddProperty(property);

            var column = CreateColumn(table, propertyDraft.Column);
            column.SetValueProperty(property);
            ApplyColumnFlags(column, propertyDraft.Column);
            columns.Add(column);
        }

        table.SetColumns(columns);

        foreach (var propertyDraft in draft.RelationProperties ?? [])
        {
            var property = new RelationProperty(
                propertyDraft.PropertyName,
                propertyDraft.CsType,
                model,
                propertyDraft.Attributes ?? []);

            property.SetCsNullable(propertyDraft.CsNullable);
            if (propertyDraft.SourceInfo.HasValue)
                property.SetSourceInfo(propertyDraft.SourceInfo.Value);

            if (propertyDraft.RelationName is not null)
                property.SetRelationName(propertyDraft.RelationName);

            ApplyAttributeSourceSpans(propertyDraft.AttributeSourceSpans, property.SetAttributeSourceSpan);
            model.AddProperty(property);
        }
    }

    private static ValueProperty CreateValueProperty(ModelDefinition model, MetadataValuePropertyDraft draft)
    {
        var property = new ValueProperty(
            draft.PropertyName,
            draft.CsType,
            model,
            draft.Attributes ?? []);

        property.SetCsNullable(draft.CsNullable);
        property.SetCsSize(draft.CsSize);
        if (draft.SourceInfo.HasValue)
            property.SetSourceInfo(draft.SourceInfo.Value);

        if (draft.EnumProperty.HasValue)
            property.SetEnumProperty(draft.EnumProperty.Value);

        ApplyAttributeSourceSpans(draft.AttributeSourceSpans, property.SetAttributeSourceSpan);
        return property;
    }

    private static ColumnDefinition CreateColumn(TableDefinition table, MetadataColumnDraft draft)
    {
        var column = new ColumnDefinition(draft.DbName, table);

        foreach (var dbType in draft.DbTypes ?? [])
            column.AddDbType(dbType.Clone());

        return column;
    }

    private static void ApplyColumnFlags(ColumnDefinition column, MetadataColumnDraft draft)
    {
        column.SetForeignKey(draft.ForeignKey);
        column.SetAutoIncrement(draft.AutoIncrement);
        column.SetNullable(draft.Nullable);

        if (draft.PrimaryKey)
            column.SetPrimaryKey();
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
