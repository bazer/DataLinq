using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("orderdetails")]
public abstract partial class Orderdetail(RowData rowData, DataSourceAccess dataSource) : Immutable<Orderdetail, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("DetailId")]
    public abstract Guid DetailId { get; }

    [ForeignKey("orders", "OrderId", "orderdetails_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("OrderId")]
    public abstract Guid? OrderId { get; }

    [ForeignKey("products", "ProductId", "orderdetails_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid? ProductId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "double", 0)]
    [Column("Discount")]
    public abstract double? Discount { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("Quantity")]
    public abstract int? Quantity { get; }

    [Relation("orders", "OrderId", "orderdetails_ibfk_1")]
    public abstract Order orders { get; }

    [Relation("products", "ProductId", "orderdetails_ibfk_2")]
    public abstract Product products { get; }

}