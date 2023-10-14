using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("productreviews")]
public partial record Productreview : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ReviewId")]
    public virtual Guid ReviewId { get; set; }

    [ForeignKey("products", "ProductId", "productreviews_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid? ProductId { get; set; }

    [ForeignKey("users", "UserId", "productreviews_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public virtual Guid? UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "tinyint")]
    [Column("Rating")]
    public virtual int? Rating { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Review")]
    public virtual string Review { get; set; }

    [Relation("products", "ProductId")]
    public virtual Product Products { get; }

    [Relation("users", "UserId")]
    public virtual User Users { get; }

}