using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("inventory")]
public interface IInventory : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("InventoryId")]
    int? InventoryId { get; set; }

    [ForeignKey("locations", "LocationId", "inventory_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    Guid? LocationId { get; set; }

    [ForeignKey("products", "ProductId", "inventory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("Stock")]
    int? Stock { get; set; }

    [Relation("locations", "LocationId", "inventory_ibfk_2")]
    ILocation locations { get; }

    [Relation("products", "ProductId", "inventory_ibfk_1")]
    IProduct products { get; }

}