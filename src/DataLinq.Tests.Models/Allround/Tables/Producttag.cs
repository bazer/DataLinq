using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("producttags")]
public partial record Producttag : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("TagId")]
    public virtual int? TagId { get; set; }

    [ForeignKey("productcategories", "CategoryId", "producttags_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("CategoryId")]
    public virtual Guid? CategoryId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Description")]
    public virtual string Description { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("TagName")]
    public virtual string TagName { get; set; }

    [Relation("productcategories", "CategoryId", "producttags_ibfk_1")]
    public virtual Productcategory productcategories { get; }

}