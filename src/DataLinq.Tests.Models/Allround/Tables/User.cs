using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("users")]
public partial record User : ITableModel<AllroundBenchmark>
{
    public enum UserRoleValue
    {
        Admin = 1,
        User = 2,
        Guest = 3,
    }
    
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public virtual Guid UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("DateJoined")]
    public virtual DateOnly? DateJoined { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("Email")]
    public virtual string Email { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "time")]
    [Column("LastLoginTime")]
    public virtual TimeOnly? LastLoginTime { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "tinyint")]
    [Column("UserAge")]
    public virtual int? UserAge { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "float")]
    [Column("UserHeight")]
    public virtual float? UserHeight { get; set; }

    [Index("idx_username", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("UserName")]
    public virtual string UserName { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("Admin", "User", "Guest")]
    [Column("UserRole")]
    public virtual UserRoleValue? UserRole { get; set; }

    [Relation("orders", "UserId", "orders_ibfk_1")]
    public virtual IEnumerable<Order> orders { get; }

    [Relation("productreviews", "UserId", "productreviews_ibfk_1")]
    public virtual IEnumerable<Productreview> productreviews { get; }

    [Relation("userfeedback", "UserId", "userfeedback_ibfk_1")]
    public virtual IEnumerable<Userfeedback> userfeedback { get; }

    [Relation("userhistory", "UserId", "userhistory_ibfk_1")]
    public virtual IEnumerable<Userhistory> userhistory { get; }

    [Relation("userprofiles", "UserId", "userprofiles_ibfk_1")]
    public virtual IEnumerable<Userprofile> userprofiles { get; }

}