using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("userfeedback")]
public abstract partial class Userfeedback(RowData rowData, DataSourceAccess dataSource) : Immutable<Userfeedback, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("FeedbackId")]
    public abstract int? FeedbackId { get; }

    [ForeignKey("products", "ProductId", "userfeedback_ibfk_2")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("ProductId")]
    public abstract Guid? ProductId { get; }

    [ForeignKey("users", "UserId", "userfeedback_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("UserId")]
    public abstract Guid? UserId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("Feedback")]
    public abstract string Feedback { get; }

    [Relation("products", "ProductId", "userfeedback_ibfk_2")]
    public abstract Product products { get; }

    [Relation("users", "UserId", "userfeedback_ibfk_1")]
    public abstract User users { get; }

}