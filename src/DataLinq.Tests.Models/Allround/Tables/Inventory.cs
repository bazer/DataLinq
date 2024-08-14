using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("inventory")]
public abstract partial class Inventory(RowData rowData, DataSourceAccess dataSource) : Immutable<Inventory>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("InventoryId")]
    public abstract int? InventoryId { get; }

    [ForeignKey("locations", "LocationId", "inventory_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    public abstract Guid? LocationId { get; }

    [ForeignKey("products", "ProductId", "inventory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid? ProductId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("Stock")]
    public abstract int? Stock { get; }

    [Relation("locations", "LocationId", "inventory_ibfk_2")]
    public abstract Location locations { get; }

    [Relation("products", "ProductId", "inventory_ibfk_1")]
    public abstract Product products { get; }

}