using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("locationshistory")]
public interface ILocationhistory : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("HistoryId")]
    Guid HistoryId { get; set; }

    [ForeignKey("locations", "LocationId", "locationshistory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    Guid? LocationId { get; set; }

    [Index("idx_changedate", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("ChangeDate")]
    DateOnly? ChangeDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("ChangeLog")]
    string ChangeLog { get; set; }

    [Relation("locations", "LocationId", "locationshistory_ibfk_1")]
    ILocation locations { get; }

}