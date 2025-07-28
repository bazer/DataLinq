using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

public partial interface IProduct
{
}

[Table("products")]
[Interface<IProduct>]
public abstract partial class Product(IRowData rowData, IDataSourceAccess dataSource) : Immutable<Product, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid ProductId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Column("CategoryId")]
    public abstract int? CategoryId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Column("ManufacturerId")]
    public abstract int? ManufacturerId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "double")]
    [Column("Price")]
    public abstract double? Price { get; }

    [Index("idx_productname", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("ProductName")]
    public abstract string ProductName { get; }

    [Relation("discounts", "ProductId", "discounts_ibfk_1")]
    public abstract IImmutableRelation<Discount> discounts { get; }

    [Relation("inventory", "ProductId", "inventory_ibfk_1")]
    public abstract IImmutableRelation<Inventory> inventory { get; }

    [Relation("orderdetails", "ProductId", "orderdetails_ibfk_2")]
    public abstract IImmutableRelation<Orderdetail> orderdetails { get; }

    [Relation("orders", "ProductId", "orders_ibfk_2")]
    public abstract IImmutableRelation<Order> orders { get; }

    [Relation("productimages", "ProductId", "productimages_ibfk_1")]
    public abstract IImmutableRelation<Productimage> productimages { get; }

    [Relation("productreviews", "ProductId", "productreviews_ibfk_2")]
    public abstract IImmutableRelation<Productreview> productreviews { get; }

    [Relation("userfeedback", "ProductId", "userfeedback_ibfk_2")]
    public abstract IImmutableRelation<Userfeedback> userfeedback { get; }

}