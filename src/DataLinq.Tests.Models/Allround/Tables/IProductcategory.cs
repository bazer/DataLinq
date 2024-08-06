using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("productcategories")]
public interface IProductcategory : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("CategoryId")]
    Guid CategoryId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CategoryName")]
    string CategoryName { get; set; }

    [Relation("producttags", "CategoryId", "producttags_ibfk_1")]
    IEnumerable<IProducttag> producttags { get; }

}