using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("productimages")]
public partial record Productimage : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ImageId")]
    public virtual Guid ImageId { get; set; }

    [ForeignKey("products", "ProductId", "productimages_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "mediumblob", 16777215)]
    [Column("ImageData")]
    public virtual Byte[] ImageData { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("ImageURL")]
    public virtual string ImageURL { get; set; }

    [Relation("products", "ProductId")]
    public virtual Product Products { get; }

}