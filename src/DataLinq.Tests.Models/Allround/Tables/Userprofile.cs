using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("userprofiles")]
public partial record Userprofile : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProfileId")]
    public virtual Guid ProfileId { get; set; }

    [ForeignKey("users", "UserId", "userprofiles_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public virtual Guid? UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Bio")]
    public virtual string Bio { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "blob", 65535)]
    [Column("ProfileImage")]
    public virtual byte[] ProfileImage { get; set; }

    [Relation("usercontacts", "ProfileId", "usercontacts_ibfk_1")]
    public virtual IEnumerable<Usercontact> usercontacts { get; }

    [Relation("users", "UserId", "userprofiles_ibfk_1")]
    public virtual User users { get; }

}