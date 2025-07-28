using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

public partial interface IProducttag
{
}

[Table("producttags")]
[Interface<IProducttag>]
public abstract partial class Producttag(IRowData rowData, IDataSourceAccess dataSource) : Immutable<Producttag, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Column("TagId")]
    public abstract int? TagId { get; }

    [ForeignKey("productcategories", "CategoryId", "producttags_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("CategoryId")]
    public abstract Guid? CategoryId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Description")]
    public abstract string Description { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("TagName")]
    public abstract string TagName { get; }

    [Relation("productcategories", "CategoryId", "producttags_ibfk_1")]
    public abstract Productcategory productcategories { get; }

}