using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.SQLite;
using DataLinq.Tests.Unit.Core;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public class SQLiteTemporalDefaultTests
{
    [Test]
    [Arguments("changed_at", "en-US")]
    [Arguments("offset_at", "en-US")]
    [Arguments("duration", "en-US")]
    [Arguments("changed_at", "ar-SA")]
    [Arguments("offset_at", "ar-SA")]
    [Arguments("duration", "ar-SA")]
    public async Task OmittedTemporalDefault_PreservesValueAndMatchesParameterStorage(string columnName, string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<TemporalDefaultsDb>().ValueOrException();
            var column = metadata.TableModels.Single().Table.GetColumnByDbName(columnName);
            var expected = column.ValueProperty.GetDefaultAttribute()!.Value;
            var ddl = new SqlFromSQLiteFactory().GetCreateTables(metadata, true).ValueOrException();
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = ddl.Text;
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO temporal_rows (id) VALUES (1)";
            command.ExecuteNonQuery();
            command.CommandText = $"INSERT INTO temporal_rows (id, \"{columnName}\") VALUES (2, @value)";
            command.Parameters.AddWithValue("@value", expected);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = $"SELECT \"{columnName}\" FROM temporal_rows ORDER BY id";
            using var reader = command.ExecuteReader();
            await Assert.That(reader.Read()).IsTrue();
            switch (expected)
            {
                case DateTime date:
                    await Assert.That(reader.GetDateTime(0).Ticks).IsEqualTo(date.Ticks);
                    break;
                case DateTimeOffset offset:
                    var actual = reader.GetFieldValue<DateTimeOffset>(0);
                    await Assert.That(actual.Ticks).IsEqualTo(offset.Ticks);
                    await Assert.That(actual.Offset).IsEqualTo(offset.Offset);
                    break;
                case TimeSpan duration:
                    await Assert.That(reader.GetFieldValue<TimeSpan>(0).Ticks).IsEqualTo(duration.Ticks);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected temporal default type: {expected.GetType()}");
            }
            var defaultText = reader.GetString(0);
            await Assert.That(reader.Read()).IsTrue();
            await Assert.That(reader.GetString(0)).IsEqualTo(defaultText);
            await Assert.That(reader.Read()).IsFalse();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
