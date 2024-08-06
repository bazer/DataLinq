using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("producttags")]
public interface IProducttag : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("TagId")]
    int? TagId { get; set; }

    [ForeignKey("productcategories", "CategoryId", "producttags_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("CategoryId")]
    Guid? CategoryId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Description")]
    string Description { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("TagName")]
    string TagName { get; set; }

    [Relation("productcategories", "CategoryId", "producttags_ibfk_1")]
    IProductcategory productcategories { get; }

}