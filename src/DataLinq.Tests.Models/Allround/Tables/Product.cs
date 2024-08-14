using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("products")]
public partial record Product : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("CategoryId")]
    public virtual int? CategoryId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ManufacturerId")]
    public virtual int? ManufacturerId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "double")]
    [Column("Price")]
    public virtual double? Price { get; set; }

    [Index("idx_productname", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("ProductName")]
    public virtual string ProductName { get; set; }

    [Relation("discounts", "ProductId", "discounts_ibfk_1")]
    public virtual IEnumerable<Discount> discounts { get; }

    [Relation("inventory", "ProductId", "inventory_ibfk_1")]
    public virtual IEnumerable<Inventory> inventory { get; }

    [Relation("orderdetails", "ProductId", "orderdetails_ibfk_2")]
    public virtual IEnumerable<Orderdetail> orderdetails { get; }

    [Relation("orders", "ProductId", "orders_ibfk_2")]
    public virtual IEnumerable<Order> orders { get; }

    [Relation("productimages", "ProductId", "productimages_ibfk_1")]
    public virtual IEnumerable<Productimage> productimages { get; }

    [Relation("productreviews", "ProductId", "productreviews_ibfk_2")]
    public virtual IEnumerable<Productreview> productreviews { get; }

    [Relation("userfeedback", "ProductId", "userfeedback_ibfk_2")]
    public virtual IEnumerable<Userfeedback> userfeedback { get; }

}