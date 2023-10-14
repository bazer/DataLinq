﻿using System;
using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("locations")]
public partial record Location : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("LocationId")]
    public virtual Guid LocationId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 500)]
    [Column("Address")]
    public virtual string Address { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("City")]
    public virtual string City { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("Country")]
    public virtual string Country { get; set; }

    [Relation("inventory", "LocationId")]
    public virtual IEnumerable<Inventory> Inventory { get; }

    [Relation("locationshistory", "LocationId")]
    public virtual IEnumerable<Locationhistory> Locationshistory { get; }

}