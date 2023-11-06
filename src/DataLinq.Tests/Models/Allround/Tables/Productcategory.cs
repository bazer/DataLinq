using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("productcategories")]
public partial record Productcategory : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("CategoryId")]
    public virtual Guid CategoryId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CategoryName")]
    public virtual string CategoryName { get; set; }

    [Relation("producttags", "CategoryId")]
    public virtual IEnumerable<Producttag> Producttags { get; }

}