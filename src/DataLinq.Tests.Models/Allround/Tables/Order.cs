using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

public partial interface IOrder
{
}

[Table("orders")]
[Interface<IOrder>]
public abstract partial class Order(IRowData rowData, IDataSourceAccess dataSource) : Immutable<Order, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
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
    public abstract Guid OrderId { get; }

    [ForeignKey("products", "ProductId", "orders_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid? ProductId { get; }

    [ForeignKey("users", "UserId", "orders_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public abstract Guid? UserId { get; }

    [Index("idx_orderdate", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("OrderDate")]
    public abstract DateOnly? OrderDate { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("Placed", "Shipped", "Delivered", "Cancelled")]
    [Column("OrderStatus")]
    public abstract OrderStatusValue? OrderStatus { get; }

    [Type(DatabaseType.MySQL, "timestamp")]
    [DefaultCurrentTimestamp]
    [Column("OrderTimestamp")]
    public abstract DateTime OrderTimestamp { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Column("ShippingCompanyId")]
    public abstract int? ShippingCompanyId { get; }

    [Relation("orderdetails", "OrderId", "orderdetails_ibfk_1")]
    public abstract IImmutableRelation<Orderdetail> orderdetails { get; }

    [Relation("payments", "OrderId", "payments_ibfk_1")]
    public abstract IImmutableRelation<Payment> payments { get; }

    [Relation("products", "ProductId", "orders_ibfk_2")]
    public abstract Product products { get; }

    [Relation("users", "UserId", "orders_ibfk_1")]
    public abstract User users { get; }

}