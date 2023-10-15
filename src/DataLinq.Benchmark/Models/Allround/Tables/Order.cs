using System;
using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("orders")]
public partial record Order : ITableModel<AllroundBenchmark>
{
    public enum OrderStatusValue
    {
        Placed = 1,
        Shipped = 2,
        Delivered = 3,
        Cancelled = 4,
    }
    
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("OrderId")]
    public virtual Guid OrderId { get; set; }

    [ForeignKey("products", "ProductId", "orders_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid? ProductId { get; set; }

    [ForeignKey("users", "UserId", "orders_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public virtual Guid? UserId { get; set; }

    [Index("idx_orderdate", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("OrderDate")]
    public virtual DateOnly? OrderDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("Placed", "Shipped", "Delivered", "Cancelled")]
    [Column("OrderStatus")]
    public virtual OrderStatusValue? OrderStatus { get; set; }

    [Type(DatabaseType.MySQL, "timestamp")]
    [Column("OrderTimestamp")]
    public virtual DateTime OrderTimestamp { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ShippingCompanyId")]
    public virtual int? ShippingCompanyId { get; set; }

    [Relation("orderdetails", "OrderId")]
    public virtual IEnumerable<Orderdetail> Orderdetails { get; }

    [Relation("payments", "OrderId")]
    public virtual IEnumerable<Payment> Payments { get; }

    [Relation("products", "ProductId")]
    public virtual Product Products { get; }

    [Relation("users", "UserId")]
    public virtual User Users { get; }

}