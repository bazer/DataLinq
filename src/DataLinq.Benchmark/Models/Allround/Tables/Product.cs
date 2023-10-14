using System;
using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

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
    [Type(DatabaseType.MySQL, "decimal")]
    [Column("Price")]
    public virtual decimal? Price { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("ProductName")]
    public virtual string ProductName { get; set; }

    [Relation("discounts", "ProductId")]
    public virtual IEnumerable<Discount> Discounts { get; }

    [Relation("inventory", "ProductId")]
    public virtual IEnumerable<Inventory> Inventory { get; }

    [Relation("orderdetails", "ProductId")]
    public virtual IEnumerable<Orderdetail> Orderdetails { get; }

    [Relation("orders", "ProductId")]
    public virtual IEnumerable<Order> Orders { get; }

    [Relation("productimages", "ProductId")]
    public virtual IEnumerable<Productimage> Productimages { get; }

    [Relation("productreviews", "ProductId")]
    public virtual IEnumerable<Productreview> Productreviews { get; }

    [Relation("userfeedback", "ProductId")]
    public virtual IEnumerable<Userfeedback> Userfeedback { get; }

}