using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("userfeedback")]
public interface IUserfeedback : ITableModel<IAllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Column("FeedbackId")]
    int? FeedbackId { get; set; }

    [ForeignKey("products", "ProductId", "userfeedback_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    Guid? ProductId { get; set; }

    [ForeignKey("users", "UserId", "userfeedback_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    Guid? UserId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Feedback")]
    string Feedback { get; set; }

    [Relation("products", "ProductId", "userfeedback_ibfk_2")]
    IProduct products { get; }

    [Relation("users", "UserId", "userfeedback_ibfk_1")]
    IUser users { get; }

}