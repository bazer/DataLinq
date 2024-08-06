using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("usercontacts")]
public interface IUsercontact : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("ContactId")]
    int? ContactId { get; set; }

    [ForeignKey("userprofiles", "ProfileId", "usercontacts_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProfileId")]
    Guid? ProfileId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "char", 30)]
    [Column("Phone")]
    string Phone { get; set; }

    [Relation("userprofiles", "ProfileId", "usercontacts_ibfk_1")]
    IUserprofile userprofiles { get; }

}