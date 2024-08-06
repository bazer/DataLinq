using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("manufacturers")]
public interface IManufacturer : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")] 
    [Column("ManufacturerId")]
    int? ManufacturerId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longblob", 4294967295)]
    [Column("Logo")]
    byte[] Logo { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("ManufacturerName")]
    string ManufacturerName { get; set; }

}