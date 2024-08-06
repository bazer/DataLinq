using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("users")]
public interface IUser : ITableModel<IAllroundBenchmark>
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
    Guid UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("DateJoined")]
    DateOnly? DateJoined { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("Email")]
    string Email { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "time")]
    [Column("LastLoginTime")]
    TimeOnly? LastLoginTime { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "tinyint")]
    [Column("UserAge")]
    int? UserAge { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "float")]
    [Column("UserHeight")]
    float? UserHeight { get; set; }

    [Index("idx_username", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("UserName")]
    string UserName { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("Admin", "User", "Guest")]
    [Column("UserRole")]
    UserRoleValue? UserRole { get; set; }

    [Relation("orders", "UserId", "orders_ibfk_1")]
    IEnumerable<IOrder> orders { get; }

    [Relation("productreviews", "UserId", "productreviews_ibfk_1")]
    IEnumerable<IProductreview> productreviews { get; }

    [Relation("userfeedback", "UserId", "userfeedback_ibfk_1")]
    IEnumerable<IUserfeedback> userfeedback { get; }

    [Relation("userhistory", "UserId", "userhistory_ibfk_1")]
    IEnumerable<IUserhistory> userhistory { get; }

    [Relation("userprofiles", "UserId", "userprofiles_ibfk_1")]
    IEnumerable<IUserprofile> userprofiles { get; }

}