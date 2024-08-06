using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("userhistory")]
public interface IUserhistory : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("HistoryId")]
    int? HistoryId { get; set; }

    [ForeignKey("users", "UserId", "userhistory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    Guid? UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "tinyblob", 255)]
    [Column("ActivityBlob")]
    byte[] ActivityBlob { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("ActivityDate")]
    DateOnly? ActivityDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("ActivityLog")]
    string ActivityLog { get; set; }

    [Relation("users", "UserId", "userhistory_ibfk_1")]
    IUser users { get; }

}