using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("shippingcompanies")]
public partial record Shippingcompany : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ShippingCompanyId")]
    public virtual int? ShippingCompanyId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CompanyName")]
    public virtual string CompanyName { get; set; }

}