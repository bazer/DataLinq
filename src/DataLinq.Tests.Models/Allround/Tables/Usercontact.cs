using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("usercontacts")]
public abstract partial class Usercontact(RowData rowData, DataSourceAccess dataSource) : Immutable<Usercontact, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("ContactId")]
    public abstract int? ContactId { get; }

    [ForeignKey("userprofiles", "ProfileId", "usercontacts_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProfileId")]
    public abstract Guid? ProfileId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "char", 30)]
    [Column("Phone")]
    public abstract string Phone { get; }

    [Relation("userprofiles", "ProfileId", "usercontacts_ibfk_1")]
    public abstract Userprofile userprofiles { get; }

}