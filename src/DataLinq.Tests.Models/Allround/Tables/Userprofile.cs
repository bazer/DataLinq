using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("userprofiles")]
public abstract partial class Userprofile(RowData rowData, DataSourceAccess dataSource) : Immutable<Userprofile, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProfileId")]
    public abstract Guid ProfileId { get; }

    [ForeignKey("users", "UserId", "userprofiles_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public abstract Guid? UserId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Bio")]
    public abstract string Bio { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "blob", 65535)]
    [Column("ProfileImage")]
    public abstract byte[] ProfileImage { get; }

    [Relation("usercontacts", "ProfileId", "usercontacts_ibfk_1")]
    public abstract IEnumerable<Usercontact> usercontacts { get; }

    [Relation("users", "UserId", "userprofiles_ibfk_1")]
    public abstract User users { get; }

}