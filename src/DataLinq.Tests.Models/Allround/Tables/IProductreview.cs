using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("productreviews")]
public interface IProductreview : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ReviewId")]
    Guid ReviewId { get; set; }

    [ForeignKey("products", "ProductId", "productreviews_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid? ProductId { get; set; }

    [ForeignKey("users", "UserId", "productreviews_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    Guid? UserId { get; set; }

    [Index("idx_rating", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "tinyint")]
    [Column("Rating")]
    int? Rating { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "mediumtext", 16777215)]
    [Column("Review")]
    string Review { get; set; }

    [Relation("products", "ProductId", "productreviews_ibfk_2")]
    IProduct products { get; }

    [Relation("users", "UserId", "productreviews_ibfk_1")]
    IUser users { get; }

}