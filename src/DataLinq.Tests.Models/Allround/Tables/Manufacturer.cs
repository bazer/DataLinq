using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("manufacturers")]
public abstract partial class Manufacturer(RowData rowData, DataSourceAccess dataSource) : Immutable<Manufacturer>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("ManufacturerId")]
    public abstract int? ManufacturerId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longblob", 4294967295)]
    [Column("Logo")]
    public abstract byte[] Logo { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("ManufacturerName")]
    public abstract string ManufacturerName { get; }

}