using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("inventory")]
public partial record Inventory : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("InventoryId")]
    public virtual int? InventoryId { get; set; }

    [ForeignKey("locations", "LocationId", "inventory_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    public virtual Guid? LocationId { get; set; }

    [ForeignKey("products", "ProductId", "inventory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("Stock")]
    public virtual int? Stock { get; set; }

    [Relation("locations", "LocationId")]
    public virtual Location Locations { get; }

    [Relation("products", "ProductId")]
    public virtual Product Products { get; }

}