using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("orderdetails")]
public partial record Orderdetail : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("DetailId")]
    public virtual Guid DetailId { get; set; }

    [ForeignKey("orders", "OrderId", "orderdetails_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("OrderId")]
    public virtual Guid? OrderId { get; set; }

    [ForeignKey("products", "ProductId", "orderdetails_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "double")]
    [Column("Discount")]
    public virtual double? Discount { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("Quantity")]
    public virtual int? Quantity { get; set; }

    [Relation("orders", "OrderId", "orderdetails_ibfk_1")]
    public virtual Order orders { get; }

    [Relation("products", "ProductId", "orderdetails_ibfk_2")]
    public virtual Product products { get; }

}