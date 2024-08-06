using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("productimages")]
public interface IProductimage : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ImageId")]
    Guid ImageId { get; set; }

    [ForeignKey("products", "ProductId", "productimages_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid? ProductId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "mediumblob", 16777215)]
    [Column("ImageData")]
    byte[] ImageData { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("ImageURL")]
    string ImageURL { get; set; }

    [Relation("products", "ProductId", "productimages_ibfk_1")]
    IProduct products { get; }

}