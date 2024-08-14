using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("discounts")]
public abstract partial class Discount(RowData rowData, DataSourceAccess dataSource) : Immutable<Discount>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("DiscountId")]
    public abstract int? DiscountId { get; }

    [ForeignKey("products", "ProductId", "discounts_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid? ProductId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "decimal", 5, 2)]
    [Column("DiscountPercentage")]
    public abstract decimal? DiscountPercentage { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date", 0)]
    [Column("EndDate")]
    public abstract DateOnly? EndDate { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date", 0)]
    [Column("StartDate")]
    public abstract DateOnly? StartDate { get; }

    [Relation("products", "ProductId", "discounts_ibfk_1")]
    public abstract Product products { get; }

}