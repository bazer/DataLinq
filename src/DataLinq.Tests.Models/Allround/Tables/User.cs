using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("users")]
public abstract partial class User(RowData rowData, DataSourceAccess dataSource) : Immutable<User, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
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
    public abstract Guid UserId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("DateJoined")]
    public abstract DateOnly? DateJoined { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("Email")]
    public abstract string Email { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "time")]
    [Column("LastLoginTime")]
    public abstract TimeOnly? LastLoginTime { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "tinyint", 4)]
    [Column("UserAge")]
    public abstract int? UserAge { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "float")]
    [Column("UserHeight")]
    public abstract float? UserHeight { get; }

    [Index("idx_username", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("UserName")]
    public abstract string UserName { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("Admin", "User", "Guest")]
    [Column("UserRole")]
    public abstract UserRoleValue? UserRole { get; }

    [Relation("orders", "UserId", "orders_ibfk_1")]
    public abstract IImmutableRelation<Order> orders { get; }

    [Relation("productreviews", "UserId", "productreviews_ibfk_1")]
    public abstract IImmutableRelation<Productreview> productreviews { get; }

    [Relation("userfeedback", "UserId", "userfeedback_ibfk_1")]
    public abstract IImmutableRelation<Userfeedback> userfeedback { get; }

    [Relation("userhistory", "UserId", "userhistory_ibfk_1")]
    public abstract IImmutableRelation<Userhistory> userhistory { get; }

    [Relation("userprofiles", "UserId", "userprofiles_ibfk_1")]
    public abstract IImmutableRelation<Userprofile> userprofiles { get; }

}