using System;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Memory.PlatformCompatibility.Smoke;

public readonly record struct MemoryPlatformGuidId(Guid Value);

public sealed class MemoryPlatformGuidIdConverter
    : DataLinqScalarConverter<MemoryPlatformGuidId, Guid>
{
    public override Guid ToProvider(
        MemoryPlatformGuidId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override MemoryPlatformGuidId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

[UseCache]
[Database("memory_platform_smoke")]
public sealed partial class MemoryPlatformSmokeDatabase(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<MemoryPlatformPrimitiveRow> PrimitiveRows { get; } = new(readSource);

    public DbRead<MemoryPlatformGuidRow> GuidRows { get; } = new(readSource);
}

[Table("memory_platform_primitive_rows")]
public abstract partial class MemoryPlatformPrimitiveRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<MemoryPlatformPrimitiveRow, MemoryPlatformSmokeDatabase>(rowData, readSource),
      ITableModel<MemoryPlatformSmokeDatabase>
{
    [PrimaryKey]
    [Column("id")]
    public abstract int Id { get; }

    [Column("group_id")]
    public abstract int GroupId { get; }

    [Column("name")]
    public abstract string Name { get; }
}

[Table("memory_platform_guid_rows")]
public abstract partial class MemoryPlatformGuidRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<MemoryPlatformGuidRow, MemoryPlatformSmokeDatabase>(rowData, readSource),
      ITableModel<MemoryPlatformSmokeDatabase>
{
    [PrimaryKey]
    [Column("id")]
    [ScalarConverter(typeof(MemoryPlatformGuidIdConverter))]
    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MySQL, "char", 32)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text32)]
    [Type(DatabaseType.MariaDB, "uuid")]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
    public abstract MemoryPlatformGuidId Id { get; }

    [Column("direct_guid")]
    [Type(DatabaseType.SQLite, "TEXT")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text36)]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
    [Type(DatabaseType.MariaDB, "char", 32)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Text32)]
    public abstract Guid DirectGuid { get; }

    [Column("name")]
    public abstract string Name { get; }
}
