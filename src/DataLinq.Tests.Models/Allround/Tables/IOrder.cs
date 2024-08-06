using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("orders")]
public interface IOrder : ITableModel<IAllroundBenchmark>
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
    Guid OrderId { get; set; }

    [ForeignKey("products", "ProductId", "orders_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid? ProductId { get; set; }

    [ForeignKey("users", "UserId", "orders_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    Guid? UserId { get; set; }

    [Index("idx_orderdate", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("OrderDate")]
    DateOnly? OrderDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("Placed", "Shipped", "Delivered", "Cancelled")]
    [Column("OrderStatus")]
    OrderStatusValue? OrderStatus { get; set; }

    [Type(DatabaseType.MySQL, "timestamp")]
    [Column("OrderTimestamp")]
    DateTime OrderTimestamp { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ShippingCompanyId")]
    int? ShippingCompanyId { get; set; }

    [Relation("orderdetails", "OrderId", "orderdetails_ibfk_1")]
    IEnumerable<IOrderdetail> orderdetails { get; }

    [Relation("payments", "OrderId", "payments_ibfk_1")]
    IEnumerable<IPayment> payments { get; }

    [Relation("products", "ProductId", "orders_ibfk_2")]
    IProduct products { get; }

    [Relation("users", "UserId", "orders_ibfk_1")]
    IUser users { get; }

}