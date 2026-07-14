using System;
using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Memory;

public readonly record struct MemoryGuidId(Guid Value);

public sealed class MemoryGuidIdConverter
    : DataLinqScalarConverter<MemoryGuidId, Guid>
{
    private static readonly object Gate = new();
    private static readonly List<string> ToProviderColumnNames = [];
    private static readonly List<string> FromProviderColumnNames = [];
    private static Action<string>? toProviderProbe;

    public static IReadOnlyList<string> ToProviderColumns
    {
        get
        {
            lock (Gate)
                return ToProviderColumnNames.ToArray();
        }
    }

    public static IReadOnlyList<string> FromProviderColumns
    {
        get
        {
            lock (Gate)
                return FromProviderColumnNames.ToArray();
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            ToProviderColumnNames.Clear();
            FromProviderColumnNames.Clear();
            toProviderProbe = null;
        }
    }

    public static void SetToProviderProbe(Action<string>? probe)
    {
        lock (Gate)
            toProviderProbe = probe;
    }

    public override Guid ToProvider(
        MemoryGuidId modelValue,
        in ScalarConversionContext context)
    {
        Action<string>? probe;
        lock (Gate)
        {
            ToProviderColumnNames.Add(context.Column.DbName);
            probe = toProviderProbe;
        }

        probe?.Invoke(context.Column.DbName);

        return modelValue.Value;
    }

    public override MemoryGuidId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context)
    {
        lock (Gate)
            FromProviderColumnNames.Add(context.Column.DbName);

        return new MemoryGuidId(providerValue);
    }
}

[UseCache]
[Database("memory_converted")]
public sealed partial class MemoryConvertedDatabase(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<MemoryConvertedRow> Rows { get; } = new(readSource);
}

[Table("memory_converted_rows")]
public abstract partial class MemoryConvertedRow :
    Immutable<MemoryConvertedRow, MemoryConvertedDatabase>,
    ITableModel<MemoryConvertedDatabase>
{
    protected MemoryConvertedRow(
        IRowData rowData,
        IDataSourceAccess dataSource)
        : base(rowData, dataSource)
    {
    }

    protected MemoryConvertedRow(
        IRowData rowData,
        IDataLinqReadSource readSource)
        : base(rowData, readSource)
    {
    }

    [PrimaryKey]
    [Column("id")]
    [ScalarConverter(typeof(MemoryGuidIdConverter))]
    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MySQL, "char", 32)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text32)]
    [Type(DatabaseType.MariaDB, "uuid")]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
    public abstract MemoryGuidId Id { get; }

    [Column("direct_guid")]
    [Type(DatabaseType.SQLite, "TEXT")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text36)]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
    [Type(DatabaseType.MariaDB, "char", 32)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Text32)]
    public abstract Guid DirectGuid { get; }

    [Column("related_id")]
    [ScalarConverter(typeof(MemoryGuidIdConverter))]
    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "uuid")]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
    public abstract MemoryGuidId RelatedId { get; }

    [Nullable]
    [Column("optional_related_id")]
    [ScalarConverter(typeof(MemoryGuidIdConverter))]
    [Type(DatabaseType.SQLite, "TEXT")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text32)]
    [Type(DatabaseType.MySQL, "char", 36)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text36)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    public abstract MemoryGuidId? OptionalRelatedId { get; }
}
