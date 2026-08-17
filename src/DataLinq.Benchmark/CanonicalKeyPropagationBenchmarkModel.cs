using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark;

public readonly record struct CanonicalKeyBenchmarkTypedId(int Value);
public sealed record CanonicalKeyBenchmarkReferenceId(string Value);

public sealed class CanonicalKeyBenchmarkTypedIdConverter
    : DataLinqScalarConverter<CanonicalKeyBenchmarkTypedId, int>
{
    public override int ToProvider(
        CanonicalKeyBenchmarkTypedId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override CanonicalKeyBenchmarkTypedId FromProvider(
        int providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

public sealed class CanonicalKeyBenchmarkReferenceIdConverter
    : DataLinqScalarConverter<CanonicalKeyBenchmarkReferenceId, string>
{
    public override string ToProvider(
        CanonicalKeyBenchmarkReferenceId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override CanonicalKeyBenchmarkReferenceId FromProvider(
        string providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

[UseCache]
[Database("canonical_key_allocation_benchmark")]
public sealed partial class CanonicalKeyBenchmarkDatabase(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<CanonicalKeyBenchmarkScalarRow> ScalarRows { get; } = new(readSource);
    public DbRead<CanonicalKeyBenchmarkCompositeRow> CompositeRows { get; } = new(readSource);
    public DbRead<CanonicalKeyBenchmarkTypedIdRow> TypedIdRows { get; } = new(readSource);
    public DbRead<CanonicalKeyBenchmarkReferenceRow> ReferenceRows { get; } = new(readSource);
    public DbRead<CanonicalKeyBenchmarkBinaryRow> BinaryRows { get; } = new(readSource);
}

[Table("allocation_scalar_keys")]
public abstract partial class CanonicalKeyBenchmarkScalarRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<CanonicalKeyBenchmarkScalarRow, CanonicalKeyBenchmarkDatabase>(rowData, readSource),
      ITableModel<CanonicalKeyBenchmarkDatabase>
{
    [PrimaryKey]
    [Column("id")]
    public abstract int Id { get; }
}

[Table("allocation_composite_keys")]
public abstract partial class CanonicalKeyBenchmarkCompositeRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<CanonicalKeyBenchmarkCompositeRow, CanonicalKeyBenchmarkDatabase>(rowData, readSource),
      ITableModel<CanonicalKeyBenchmarkDatabase>
{
    [PrimaryKey]
    [Column("tenant_id")]
    public abstract int TenantId { get; }

    [PrimaryKey]
    [Column("code")]
    public abstract string Code { get; }
}

[Table("allocation_typed_id_keys")]
public abstract partial class CanonicalKeyBenchmarkTypedIdRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<CanonicalKeyBenchmarkTypedIdRow, CanonicalKeyBenchmarkDatabase>(rowData, readSource),
      ITableModel<CanonicalKeyBenchmarkDatabase>
{
    [PrimaryKey]
    [Column("id")]
    [ScalarConverter(typeof(CanonicalKeyBenchmarkTypedIdConverter))]
    public abstract CanonicalKeyBenchmarkTypedId Id { get; }
}

[Table("allocation_converter_keys")]
public abstract partial class CanonicalKeyBenchmarkReferenceRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<CanonicalKeyBenchmarkReferenceRow, CanonicalKeyBenchmarkDatabase>(rowData, readSource),
      ITableModel<CanonicalKeyBenchmarkDatabase>
{
    [PrimaryKey]
    [Column("id")]
    [ScalarConverter(typeof(CanonicalKeyBenchmarkReferenceIdConverter))]
    public abstract CanonicalKeyBenchmarkReferenceId Id { get; }
}

[Table("allocation_binary_keys")]
public abstract partial class CanonicalKeyBenchmarkBinaryRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<CanonicalKeyBenchmarkBinaryRow, CanonicalKeyBenchmarkDatabase>(rowData, readSource),
      ITableModel<CanonicalKeyBenchmarkDatabase>
{
    [PrimaryKey]
    [Column("id")]
    public abstract byte[] Id { get; }
}
