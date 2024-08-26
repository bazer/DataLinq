using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("payments")]
public abstract partial class Payment(RowData rowData, DataSourceAccess dataSource) : Immutable<Payment, AllroundBenchmark>(rowData, dataSource), ITableModel<AllroundBenchmark>
{
    public enum PaymentMethodValue
    {
        CreditCard = 1,
        DebitCard = 2,
        PayPal = 3,
        BankTransfer = 4,
    }
    
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Column("PaymentId")]
    public abstract int? PaymentId { get; }

    [ForeignKey("orders", "OrderId", "payments_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("OrderId")]
    public abstract Guid? OrderId { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "decimal", 10, 2)]
    [Column("Amount")]
    public abstract decimal? Amount { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date", 0)]
    [Column("PaymentDate")]
    public abstract DateOnly? PaymentDate { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("PaymentDetails")]
    public abstract string PaymentDetails { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("CreditCard", "DebitCard", "PayPal", "BankTransfer")]
    [Column("PaymentMethod")]
    public abstract PaymentMethodValue? PaymentMethod { get; }

    [Relation("orders", "OrderId", "payments_ibfk_1")]
    public abstract Order orders { get; }

}