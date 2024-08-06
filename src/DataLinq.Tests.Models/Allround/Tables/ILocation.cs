using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("locations")]
public interface ILocation : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    Guid LocationId { get; set; }

    [Index("idx_address", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 500)]
    [Column("Address")]
    string Address { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("City")]
    string City { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("Country")]
    string Country { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "float")]
    [Column("Latitude")]
    float? Latitude { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "float")]
    [Column("Longitude")]
    float? Longitude { get; set; }

    [Relation("inventory", "LocationId", "inventory_ibfk_2")]
    IEnumerable<IInventory> inventory { get; }

    [Relation("locationshistory", "LocationId", "locationshistory_ibfk_1")]
    IEnumerable<ILocationhistory> locationshistory { get; }

}