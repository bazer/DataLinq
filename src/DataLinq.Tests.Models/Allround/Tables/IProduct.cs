using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("products")]
public interface IProduct : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("CategoryId")]
    int? CategoryId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ManufacturerId")]
    int? ManufacturerId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "double")]
    [Column("Price")]
    double? Price { get; set; }

    [Index("idx_productname", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("ProductName")]
    string ProductName { get; set; }

    [Relation("discounts", "ProductId", "discounts_ibfk_1")]
    IEnumerable<IDiscount> discounts { get; }

    [Relation("inventory", "ProductId", "inventory_ibfk_1")]
    IEnumerable<IInventory> inventory { get; }

    [Relation("orderdetails", "ProductId", "orderdetails_ibfk_2")]
    IEnumerable<IOrderdetail> orderdetails { get; }

    [Relation("orders", "ProductId", "orders_ibfk_2")]
    IEnumerable<IOrder> orders { get; }

    [Relation("productimages", "ProductId", "productimages_ibfk_1")]
    IEnumerable<IProductimage> productimages { get; }

    [Relation("productreviews", "ProductId", "productreviews_ibfk_2")]
    IEnumerable<IProductreview> productreviews { get; }

    [Relation("userfeedback", "ProductId", "userfeedback_ibfk_2")]
    IEnumerable<IUserfeedback> userfeedback { get; }

}