using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("payments")]
public interface IPayment : ITableModel<IAllroundBenchmark>
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
    [Type(DatabaseType.MySQL, "int")]
    [Column("PaymentId")]
    int? PaymentId { get; set; }

    [ForeignKey("orders", "OrderId", "payments_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("OrderId")]
    Guid? OrderId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "decimal", 10, 2)]
    [Column("Amount")]
    decimal? Amount { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("PaymentDate")]
    DateOnly? PaymentDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("PaymentDetails")]
    string PaymentDetails { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("CreditCard", "DebitCard", "PayPal", "BankTransfer")]
    [Column("PaymentMethod")]
    PaymentMethodValue? PaymentMethod { get; set; }

    [Relation("orders", "OrderId", "payments_ibfk_1")]
    IOrder orders { get; }

}