using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("locationshistory")]
public abstract partial class Locationhistory(RowData rowData, DataSourceAccess dataSource) : Immutable<Locationhistory>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("HistoryId")]
    public abstract Guid HistoryId { get; }

    [ForeignKey("locations", "LocationId", "locationshistory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    public abstract Guid? LocationId { get; }

    [Index("idx_changedate", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "date", 0)]
    [Column("ChangeDate")]
    public abstract DateOnly? ChangeDate { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("ChangeLog")]
    public abstract string ChangeLog { get; }

    [Relation("locations", "LocationId", "locationshistory_ibfk_1")]
    public abstract Location locations { get; }

}