using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("userhistory")]
public partial record Userhistory : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("HistoryId")]
    public virtual int? HistoryId { get; set; }

    [ForeignKey("users", "UserId", "userhistory_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public virtual Guid? UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("ActivityDate")]
    public virtual DateOnly? ActivityDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("ActivityLog")]
    public virtual string ActivityLog { get; set; }

    [Relation("users", "UserId")]
    public virtual User Users { get; }

}