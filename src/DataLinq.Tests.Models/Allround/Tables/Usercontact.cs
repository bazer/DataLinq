using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("usercontacts")]
public partial record Usercontact : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ContactId")]
    public virtual int? ContactId { get; set; }

    [ForeignKey("userprofiles", "ProfileId", "usercontacts_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProfileId")]
    public virtual Guid? ProfileId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "char", 30)]
    [Column("Phone")]
    public virtual string Phone { get; set; }

    [Relation("userprofiles", "ProfileId", "usercontacts_ibfk_1")]
    public virtual Userprofile userprofiles { get; }

}