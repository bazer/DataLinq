using System;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark;

public readonly record struct MemoryBenchmarkGuidId(Guid Value);

public sealed class MemoryBenchmarkGuidIdConverter
    : DataLinqScalarConverter<MemoryBenchmarkGuidId, Guid>
{
    public override Guid ToProvider(
        MemoryBenchmarkGuidId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override MemoryBenchmarkGuidId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

[UseCache]
[Database("memory_benchmark")]
public sealed partial class MemoryBenchmarkDatabase(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<MemoryBenchmarkRow> Rows { get; } = new(readSource);

    public DbRead<MemoryBenchmarkGuidRow> GuidRows { get; } = new(readSource);
}

[Table("memory_benchmark_rows")]
public abstract partial class MemoryBenchmarkRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<MemoryBenchmarkRow, MemoryBenchmarkDatabase>(rowData, readSource),
      ITableModel<MemoryBenchmarkDatabase>
{
    [PrimaryKey]
    [Column("id")]
    public abstract int Id { get; }

    [Column("group_id")]
    public abstract int GroupId { get; }

    [Column("name")]
    public abstract string Name { get; }
}

[Table("memory_benchmark_guid_rows")]
public abstract partial class MemoryBenchmarkGuidRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<MemoryBenchmarkGuidRow, MemoryBenchmarkDatabase>(rowData, readSource),
      ITableModel<MemoryBenchmarkDatabase>
{
    [PrimaryKey]
    [Column("id")]
    [ScalarConverter(typeof(MemoryBenchmarkGuidIdConverter))]
    public abstract MemoryBenchmarkGuidId Id { get; }

    [Column("direct_guid")]
    public abstract Guid DirectGuid { get; }

    [Column("name")]
    public abstract string Name { get; }
}
