using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

public partial interface IShippingcompany
{
}

[Table("shippingcompanies")]
[Interface<IShippingcompany>]
public abstract partial class Shippingcompany(RowData rowData, DataSourceAccess dataSource) : Immutable<Shippingcompany, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Column("ShippingCompanyId")]
    public abstract int? ShippingCompanyId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CompanyName")]
    public abstract string CompanyName { get; }

}