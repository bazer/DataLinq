﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("productcategories")]
public abstract partial class Productcategory(RowData rowData, DataSourceAccess dataSource) : Immutable<Productcategory, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("CategoryId")]
    public abstract Guid CategoryId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CategoryName")]
    public abstract string CategoryName { get; }

    [Relation("producttags", "CategoryId", "producttags_ibfk_1")]
    public abstract IEnumerable<Producttag> producttags { get; }

}