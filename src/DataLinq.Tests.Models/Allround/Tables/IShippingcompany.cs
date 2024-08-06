using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("shippingcompanies")]
public interface IShippingcompany : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ShippingCompanyId")]
    int? ShippingCompanyId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CompanyName")]
    string CompanyName { get; set; }

}