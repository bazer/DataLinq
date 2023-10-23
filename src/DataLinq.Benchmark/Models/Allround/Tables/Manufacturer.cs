using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("manufacturers")]
public partial record Manufacturer : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ManufacturerId")]
    public virtual int? ManufacturerId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longblob", 4294967295)]
    [Column("Logo")]
    public virtual Byte[] Logo { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("ManufacturerName")]
    public virtual string ManufacturerName { get; set; }

}