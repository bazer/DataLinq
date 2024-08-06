using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("discounts")]
public interface IDiscount : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("DiscountId")]
    int? DiscountId { get; set; }

    [ForeignKey("products", "ProductId", "discounts_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "decimal", 5, 2)]
    [Column("DiscountPercentage")]
    decimal? DiscountPercentage { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("EndDate")]
    DateOnly? EndDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("StartDate")]
    DateOnly? StartDate { get; set; }

    [Relation("products", "ProductId", "discounts_ibfk_1")]
    IProduct products { get; }

}