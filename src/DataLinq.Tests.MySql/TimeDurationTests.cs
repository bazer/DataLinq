using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public class TimeDurationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task TimeOnlyRejectsDurationsOutsideItsRangeWithoutWrapping(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(TimeOnlyRejectsDurationsOutsideItsRangeWithoutWrapping));
        using var connection = new MySqlConnection(schema.Connection.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST('25:00:00' AS TIME), CAST('-01:00:00' AS TIME), CAST('24:00:00' AS TIME), CAST('-00:00:00.000001' AS TIME(6)), CAST('00:00:00' AS TIME), CAST('23:59:59.999999' AS TIME(6))";
        using var reader = new SqlDataLinqDataReader(command.ExecuteReader());
        await Assert.That(reader.ReadNextRow()).IsTrue();

        for (var ordinal = 0; ordinal < 4; ordinal++)
        {
            var index = ordinal;
            await Assert.That(() => reader.GetTimeOnly(index)).Throws<InvalidCastException>();
        }
        await Assert.That(reader.GetTimeOnly(4)).IsEqualTo(TimeOnly.MinValue);
        await Assert.That(reader.GetTimeOnly(5).Ticks).IsEqualTo(TimeSpan.TicksPerDay - 10);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ExplicitTimeSpanMappingPreservesDurationsAndNulls(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<DurationTestDatabase>.Create(
            provider, nameof(ExplicitTimeSpanMappingPreservesDurationsAndNulls));
        var values = new[] { TimeSpan.FromHours(25), TimeSpan.FromHours(-1), TimeSpan.FromHours(838) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59) };
        for (var index = 0; index < values.Length; index++)
            scope.Database.Insert(new MutableDurationTestRow { Id = index + 1, Duration = values[index], OptionalDuration = index == 0 ? (TimeSpan?)null : values[index] });

        scope.Database.Provider.State.ClearCache();
        var rows = scope.Database.Query().Rows.OrderBy(row => row.Id).ToArray();
        await Assert.That(rows.Length).IsEqualTo(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            await Assert.That(rows[index].Duration).IsEqualTo(values[index]);
            await Assert.That(rows[index].OptionalDuration).IsEqualTo(index == 0 ? (TimeSpan?)null : values[index]);
        }
    }
}

public partial class DurationTestDatabase(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<DurationTestRow> Rows { get; } = new(source);
}

[Table("duration_rows")]
public abstract partial class DurationTestRow(IRowData data, IDataSourceAccess source)
    : Immutable<DurationTestRow, DurationTestDatabase>(data, source), ITableModel<DurationTestDatabase>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int")]
    public abstract int Id { get; }

    [Column("duration"), Type(DatabaseType.MySQL, "time"), Type(DatabaseType.MariaDB, "time")]
    public abstract TimeSpan Duration { get; }

    [Nullable, Column("optional_duration"), Type(DatabaseType.MySQL, "time"), Type(DatabaseType.MariaDB, "time")]
    public abstract TimeSpan? OptionalDuration { get; }
}
