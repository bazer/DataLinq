using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Benchmark;

public readonly record struct ExactTerminalBenchmarkId(int Value);

public sealed class ExactTerminalBenchmarkIdConverter
    : DataLinqScalarConverter<ExactTerminalBenchmarkId, int>
{
    public override int ToProvider(
        ExactTerminalBenchmarkId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override ExactTerminalBenchmarkId FromProvider(
        int providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

[UseCache]
[Database("typed_id_exact_terminal_benchmark")]
public sealed partial class TypedIdExactTerminalBenchmarkDatabase(DataSourceAccess dataSource)
    : IDatabaseModel
{
    public DbRead<TypedIdExactTerminalBenchmarkRow> Rows { get; } = new(dataSource);
}

[Table("typed_id_exact_terminal_rows")]
public abstract partial class TypedIdExactTerminalBenchmarkRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<TypedIdExactTerminalBenchmarkRow, TypedIdExactTerminalBenchmarkDatabase>(rowData, dataSource),
      ITableModel<TypedIdExactTerminalBenchmarkDatabase>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(ExactTerminalBenchmarkIdConverter))]
    [Column("id")]
    public abstract ExactTerminalBenchmarkId Id { get; }
}
