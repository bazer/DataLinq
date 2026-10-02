using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class PartialCacheMetadataTests
{
    [Test]
    [Arguments(typeof(PartialCachedDb), true)]
    [Arguments(typeof(PartialUncachedDb), false)]
    public async Task GeneratedMetadata_MatchesCompiledPartialAttributes(Type databaseType, bool expected)
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(databaseType).ValueOrException();
        var databaseAttribute = databaseType.GetCustomAttributes(typeof(UseCacheAttribute), true).Cast<UseCacheAttribute>().Single();
        await Assert.That(metadata.UseCache).IsEqualTo(expected);
        await Assert.That(metadata.Attributes.OfType<UseCacheAttribute>().Single().UseCache).IsEqualTo(databaseAttribute.UseCache);
        foreach (var model in metadata.TableModels)
        {
            var modelType = model.Model.CsType.Type!;
            var attribute = modelType.GetCustomAttributes(typeof(UseCacheAttribute), true).Cast<UseCacheAttribute>().SingleOrDefault();
            await Assert.That(model.Table.UseCache).IsEqualTo(attribute?.UseCache ?? expected);
            await Assert.That(model.Model.Attributes.OfType<UseCacheAttribute>().SingleOrDefault()?.UseCache).IsEqualTo(attribute?.UseCache);
        }
        await Assert.That(metadata.IsFrozen).IsTrue();
    }
}

[Database("partial_cached")]
public partial class PartialCachedDb(DataSourceAccess access) : IDatabaseModel<PartialCachedDb>
{
    public DbRead<PartialOptOutRow> OptOut { get; } = new(access);
    public DbRead<PartialInheritRow> Inherited { get; } = new(access);
}

[Table("opt_out")]
public abstract partial class PartialOptOutRow(IRowData row, IDataSourceAccess access)
    : Immutable<PartialOptOutRow, PartialCachedDb>(row, access), ITableModel<PartialCachedDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
}

[Table("inherited")]
public abstract partial class PartialInheritRow(IRowData row, IDataSourceAccess access)
    : Immutable<PartialInheritRow, PartialCachedDb>(row, access), ITableModel<PartialCachedDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
}

[Database("partial_uncached")]
public partial class PartialUncachedDb(DataSourceAccess access) : IDatabaseModel<PartialUncachedDb>
{
    public DbRead<PartialOptInRow> OptIn { get; } = new(access);
}

[Table("opt_in")]
public abstract partial class PartialOptInRow(IRowData row, IDataSourceAccess access)
    : Immutable<PartialOptInRow, PartialUncachedDb>(row, access), ITableModel<PartialUncachedDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
}
