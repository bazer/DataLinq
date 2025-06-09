using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

public partial interface IProductimage
{
}

[Table("productimages")]
[Interface<IProductimage>]
public abstract partial class Productimage(RowData rowData, DataSourceAccess dataSource) : Immutable<Productimage, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ImageId")]
    public abstract Guid ImageId { get; }

    [ForeignKey("products", "ProductId", "productimages_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid? ProductId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "mediumblob", 16777215)]
    [Column("ImageData")]
    public abstract byte[] ImageData { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("ImageURL")]
    public abstract string ImageURL { get; }

    [Relation("products", "ProductId", "productimages_ibfk_1")]
    public abstract Product products { get; }

}