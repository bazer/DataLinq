using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class JoinKeyCompatibilityTranslationTests
{
    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DifferentConverterMappings_RejectBeforeSqlOrConverterExecution(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<NominalJoinCompatibilityDb>.Create(
            provider,
            nameof(DifferentConverterMappings_RejectBeforeSqlOrConverterExecution));

        try
        {
            NominalJoinLeftIdConverter.Reset();
            NominalJoinRightIdConverter.Reset();
            DataLinqMetrics.Reset();

            var exception = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().LeftRows
                    .Join(
                        databaseScope.Database.Query().RightRows,
                        left => left.Id,
                        right => right.Id,
                        (left, _) => left.Id)
                    .ToList());
            var snapshot = DataLinqMetrics.Snapshot();

            await Assert.That(exception.Message).Contains("SQL Inner join key compatibility failed");
            await Assert.That(exception.Message).Contains("ExplicitJoin");
            await Assert.That(exception.Message).Contains(provider.DatabaseType.ToString());
            await Assert.That(exception.Message).Contains("nominal_join_left.id");
            await Assert.That(exception.Message).Contains("nominal_join_right.id");
            await Assert.That(exception.Message).Contains(nameof(NominalJoinLeftIdConverter));
            await Assert.That(exception.Message).Contains(nameof(NominalJoinRightIdConverter));
            await Assert.That(exception.Message).Contains("scalar converter CLR types differ");
            await Assert.That(snapshot.Commands.TotalExecutions).IsEqualTo(0);
            await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(0);
            await Assert.That(NominalJoinLeftIdConverter.TotalCalls).IsEqualTo(0);
            await Assert.That(NominalJoinRightIdConverter.TotalCalls).IsEqualTo(0);
        }
        finally
        {
            DataLinqMetrics.Reset();
            NominalJoinLeftIdConverter.Reset();
            NominalJoinRightIdConverter.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DifferentGuidStorageFormats_RejectBeforeSqlExecution(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<GuidJoinCompatibilityDb>.Create(
            provider,
            nameof(DifferentGuidStorageFormats_RejectBeforeSqlExecution));

        try
        {
            DataLinqMetrics.Reset();

            var exception = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().LittleEndianRows
                    .Join(
                        databaseScope.Database.Query().RfcRows,
                        left => left.Id,
                        right => right.Id,
                        (left, _) => left.Id)
                    .ToList());
            var snapshot = DataLinqMetrics.Snapshot();

            await Assert.That(exception.Message).Contains("SQL Inner join key compatibility failed");
            await Assert.That(exception.Message).Contains("ExplicitJoin");
            await Assert.That(exception.Message).Contains(provider.DatabaseType.ToString());
            await Assert.That(exception.Message).Contains("guid_join_little_endian.id");
            await Assert.That(exception.Message).Contains("guid_join_rfc.id");
            await Assert.That(exception.Message)
                .Contains(nameof(GuidStorageFormat.Binary16LittleEndian));
            await Assert.That(exception.Message)
                .Contains(nameof(GuidStorageFormat.Binary16Rfc4122));
            await Assert.That(exception.Message).Contains("UUID storage formats differ");
            await Assert.That(snapshot.Commands.TotalExecutions).IsEqualTo(0);
            await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(0);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }
}

public readonly record struct NominalJoinId(int Value);

public sealed class NominalJoinLeftIdConverter : DataLinqScalarConverter<NominalJoinId, int>
{
    public static int TotalCalls { get; private set; }

    public static void Reset() => TotalCalls = 0;

    public override int ToProvider(NominalJoinId modelValue, in ScalarConversionContext context)
    {
        TotalCalls++;
        return modelValue.Value;
    }

    public override NominalJoinId FromProvider(int providerValue, in ScalarConversionContext context)
    {
        TotalCalls++;
        return new NominalJoinId(providerValue);
    }
}

public sealed class NominalJoinRightIdConverter : DataLinqScalarConverter<NominalJoinId, int>
{
    public static int TotalCalls { get; private set; }

    public static void Reset() => TotalCalls = 0;

    public override int ToProvider(NominalJoinId modelValue, in ScalarConversionContext context)
    {
        TotalCalls++;
        return checked(modelValue.Value + 1);
    }

    public override NominalJoinId FromProvider(int providerValue, in ScalarConversionContext context)
    {
        TotalCalls++;
        return new NominalJoinId(checked(providerValue - 1));
    }
}

[Database("nominaljoincompatibility")]
public sealed partial class NominalJoinCompatibilityDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<NominalJoinLeftRow> LeftRows { get; } = new(dataSource);
    public DbRead<NominalJoinRightRow> RightRows { get; } = new(dataSource);
}

[Table("nominal_join_left")]
public abstract partial class NominalJoinLeftRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<NominalJoinLeftRow, NominalJoinCompatibilityDb>(rowData, dataSource),
      ITableModel<NominalJoinCompatibilityDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(NominalJoinLeftIdConverter))]
    [Column("id")]
    public abstract NominalJoinId Id { get; }
}

[Database("guidjoincompatibility")]
public sealed partial class GuidJoinCompatibilityDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<GuidJoinLittleEndianRow> LittleEndianRows { get; } = new(dataSource);
    public DbRead<GuidJoinRfcRow> RfcRows { get; } = new(dataSource);
}

[Table("guid_join_little_endian")]
public abstract partial class GuidJoinLittleEndianRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<GuidJoinLittleEndianRow, GuidJoinCompatibilityDb>(rowData, dataSource),
      ITableModel<GuidJoinCompatibilityDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "BLOB")]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16LittleEndian)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16LittleEndian)]
    [Column("id")]
    public abstract Guid Id { get; }
}

[Table("guid_join_rfc")]
public abstract partial class GuidJoinRfcRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<GuidJoinRfcRow, GuidJoinCompatibilityDb>(rowData, dataSource),
      ITableModel<GuidJoinCompatibilityDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "BLOB")]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [Column("id")]
    public abstract Guid Id { get; }
}

[Table("nominal_join_right")]
public abstract partial class NominalJoinRightRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<NominalJoinRightRow, NominalJoinCompatibilityDb>(rowData, dataSource),
      ITableModel<NominalJoinCompatibilityDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(NominalJoinRightIdConverter))]
    [Column("id")]
    public abstract NominalJoinId Id { get; }
}
