using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("userprofiles")]
public interface IUserprofile : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProfileId")]
    Guid ProfileId { get; set; }

    [ForeignKey("users", "UserId", "userprofiles_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    Guid? UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Bio")]
    string Bio { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "blob", 65535)]
    [Column("ProfileImage")]
    byte[] ProfileImage { get; set; }

    [Relation("usercontacts", "ProfileId", "usercontacts_ibfk_1")]
    IEnumerable<IUsercontact> usercontacts { get; }

    [Relation("users", "UserId", "userprofiles_ibfk_1")]
    IUser users { get; }

}