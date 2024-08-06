using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("orderdetails")]
public interface IOrderdetail : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("DetailId")]
    Guid DetailId { get; set; }

    [ForeignKey("orders", "OrderId", "orderdetails_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("OrderId")]
    Guid? OrderId { get; set; }

    [ForeignKey("products", "ProductId", "orderdetails_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "double")]
    [Column("Discount")]
    double? Discount { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("Quantity")]
    int? Quantity { get; set; }

    [Relation("orders", "OrderId", "orderdetails_ibfk_1")]
    IOrder orders { get; }

    [Relation("products", "ProductId", "orderdetails_ibfk_2")]
    IProduct products { get; }

}