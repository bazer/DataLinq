using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public class TemporalDefaultAttributeTests
{
    [Test]
    public async Task TemporalCarriers_PreserveTicksKindOffsetAndCultureIndependence()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            foreach (var kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Utc, DateTimeKind.Local })
            {
                var date = (DateTime)new DefaultDateTimeAttribute("1970-01-01T00:00:00.1234567", kind).Value;
                await Assert.That(date.Ticks).IsEqualTo(new DateTime(1970, 1, 1).Ticks + 1234567);
                await Assert.That(date.Kind).IsEqualTo(kind);
            }
            var offset = (DateTimeOffset)new DefaultDateTimeOffsetAttribute("2020-02-03T04:05:06.7654321+05:30").Value;
            await Assert.That(offset.Offset).IsEqualTo(TimeSpan.FromHours(5.5));
            await Assert.That(offset.Ticks % TimeSpan.TicksPerSecond).IsEqualTo(7654321L);
            var duration = (TimeSpan)new DefaultTimeSpanAttribute("-1.01:00:00.1234567").Value;
            await Assert.That(duration.Ticks).IsEqualTo(-25 * TimeSpan.TicksPerHour - 1234567);
            await Assert.That(() => new DefaultDateTimeAttribute("1970-01-01T00:00:00Z")).Throws<FormatException>();
            await Assert.That(() => new DefaultDateTimeOffsetAttribute("2020-02-03T04:05:06")).Throws<FormatException>();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public async Task GeneratedTemporalDefaults_HaveTypedMetadataAndExactMutableInitialization()
    {
        var result = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<TemporalDefaultsDb>();
        await Assert.That(result.HasFailed).IsFalse();
        var table = result.Value.TableModels.Single().Table;
        var mutable = new MutableTemporalDefaultsRow { Id = 1 };
        await Assert.That(mutable.ChangedAt.Ticks % TimeSpan.TicksPerSecond).IsEqualTo(1234567L);
        await Assert.That(mutable.ChangedAt.Kind).IsEqualTo(DateTimeKind.Unspecified);
        await Assert.That(mutable.OffsetAt.Offset).IsEqualTo(TimeSpan.FromHours(5.5));
        await Assert.That(mutable.OffsetAt.Ticks % TimeSpan.TicksPerSecond).IsEqualTo(7654321L);
        await Assert.That(mutable.Duration.Ticks).IsEqualTo(-25 * TimeSpan.TicksPerHour - 1234567);
        await Assert.That(table.Columns.Single(x => x.DbName == "changed_at").ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo(mutable.ChangedAt);
    }
}

[Database("temporal_defaults")]
public partial class TemporalDefaultsDb(DataSourceAccess source) : IDatabaseModel<TemporalDefaultsDb>
{
    public DbRead<TemporalDefaultsRow> Rows { get; } = new(source);
}

[Table("temporal_rows")]
public abstract partial class TemporalDefaultsRow(IRowData data, IDataSourceAccess source)
    : Immutable<TemporalDefaultsRow, TemporalDefaultsDb>(data, source), ITableModel<TemporalDefaultsDb>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.SQLite, "INTEGER")]
    public abstract int Id { get; }
    [Column("changed_at"), Type(DatabaseType.SQLite, "TEXT"), DefaultDateTime("1970-01-01T00:00:00.1234567")]
    public abstract DateTime ChangedAt { get; }
    [Column("offset_at"), Type(DatabaseType.SQLite, "TEXT"), DefaultDateTimeOffset("2020-02-03T04:05:06.7654321+05:30")]
    public abstract DateTimeOffset OffsetAt { get; }
    [Column("duration"), Type(DatabaseType.SQLite, "TEXT"), DefaultTimeSpan("-1.01:00:00.1234567")]
    public abstract TimeSpan Duration { get; }
}
