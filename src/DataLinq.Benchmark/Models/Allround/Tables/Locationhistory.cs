using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("locationshistory")]
public partial record Locationhistory : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("HistoryId")]
    public virtual Guid HistoryId { get; set; }

    [ForeignKey("locations", "LocationId", "locationshistory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    public virtual Guid? LocationId { get; set; }

    [Index("idx_changedate", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("ChangeDate")]
    public virtual DateOnly? ChangeDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("ChangeLog")]
    public virtual string ChangeLog { get; set; }

    [Relation("locations", "LocationId")]
    public virtual Location Locations { get; }

}