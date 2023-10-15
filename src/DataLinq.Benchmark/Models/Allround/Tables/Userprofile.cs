using System;
using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

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
    public virtual Byte[] ProfileImage { get; set; }

    [Relation("usercontacts", "ProfileId")]
    public virtual IEnumerable<Usercontact> Usercontacts { get; }

    [Relation("users", "UserId")]
    public virtual User Users { get; }

}