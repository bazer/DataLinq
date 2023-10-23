using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

[Table("userfeedback")]
public partial record Userfeedback : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("FeedbackId")]
    public virtual int? FeedbackId { get; set; }

    [ForeignKey("products", "ProductId", "userfeedback_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public virtual Guid? ProductId { get; set; }

    [ForeignKey("users", "UserId", "userfeedback_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public virtual Guid? UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Feedback")]
    public virtual string Feedback { get; set; }

    [Relation("products", "ProductId")]
    public virtual Product Products { get; }

    [Relation("users", "UserId")]
    public virtual User Users { get; }

}