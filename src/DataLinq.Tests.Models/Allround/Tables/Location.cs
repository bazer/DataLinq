using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

public partial interface ILocation
{
}

[Table("locations")]
[Interface<ILocation>]
public abstract partial class Location(RowData rowData, DataSourceAccess dataSource) : Immutable<Location, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    public abstract Guid LocationId { get; }

    [Index("idx_address", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 500)]
    [Column("Address")]
    public abstract string Address { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("City")]
    public abstract string City { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("Country")]
    public abstract string Country { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "float")]
    [Column("Latitude")]
    public abstract float? Latitude { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "float")]
    [Column("Longitude")]
    public abstract float? Longitude { get; }

    [Relation("inventory", "LocationId", "inventory_ibfk_2")]
    public abstract IImmutableRelation<Inventory> inventory { get; }

    [Relation("locationshistory", "LocationId", "locationshistory_ibfk_1")]
    public abstract IImmutableRelation<Locationhistory> locationshistory { get; }

}