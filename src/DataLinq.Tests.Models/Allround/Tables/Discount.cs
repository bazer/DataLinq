using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("discounts")]
public partial record Discount : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("DiscountId")]
    public virtual int? DiscountId { get; set; }

    [ForeignKey("products", "ProductId", "discounts_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "decimal", 5, 2)]
    [Column("DiscountPercentage")]
    public virtual decimal? DiscountPercentage { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("EndDate")]
    public virtual DateOnly? EndDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("StartDate")]
    public virtual DateOnly? StartDate { get; set; }

    [Relation("products", "ProductId", "discounts_ibfk_1")]
    public virtual Product products { get; }

}