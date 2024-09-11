using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("userhistory")]
public abstract partial class Userhistory(RowData rowData, DataSourceAccess dataSource) : Immutable<Userhistory, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Column("HistoryId")]
    public abstract int? HistoryId { get; }

    [ForeignKey("users", "UserId", "userhistory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public abstract Guid? UserId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "tinyblob", 255)]
    [Column("ActivityBlob")]
    public abstract byte[] ActivityBlob { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("ActivityDate")]
    public abstract DateOnly? ActivityDate { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("ActivityLog")]
    public abstract string ActivityLog { get; }

    [Relation("users", "UserId", "userhistory_ibfk_1")]
    public abstract User users { get; }

}