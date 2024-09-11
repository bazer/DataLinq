using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("productreviews")]
public abstract partial class Productreview(RowData rowData, DataSourceAccess dataSource) : Immutable<Productreview, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ReviewId")]
    public abstract Guid ReviewId { get; }

    [ForeignKey("products", "ProductId", "productreviews_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid? ProductId { get; }

    [ForeignKey("users", "UserId", "productreviews_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public abstract Guid? UserId { get; }

    [Index("idx_rating", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "tinyint", 4)]
    [Column("Rating")]
    public abstract int? Rating { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "mediumtext", 16777215)]
    [Column("Review")]
    public abstract string Review { get; }

    [Relation("products", "ProductId", "productreviews_ibfk_2")]
    public abstract Product products { get; }

    [Relation("users", "UserId", "productreviews_ibfk_1")]
    public abstract User users { get; }

}