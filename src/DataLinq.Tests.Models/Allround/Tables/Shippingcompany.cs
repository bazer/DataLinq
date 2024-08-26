using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("shippingcompanies")]
public abstract partial class Shippingcompany(RowData rowData, DataSourceAccess dataSource) : Immutable<Shippingcompany, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("ShippingCompanyId")]
    public abstract int? ShippingCompanyId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CompanyName")]
    public abstract string CompanyName { get; }

}