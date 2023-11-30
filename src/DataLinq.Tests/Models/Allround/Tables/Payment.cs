using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Allround;

[Table("payments")]
public partial record Payment : ITableModel<AllroundBenchmark>
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
    public virtual int? PaymentId { get; set; }

    [ForeignKey("orders", "OrderId", "payments_ibfk_1")]
    [Nullable]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("OrderId")]
    public virtual Guid? OrderId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "decimal", 10, 2)]
    [Column("Amount")]
    public virtual decimal? Amount { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("PaymentDate")]
    public virtual DateOnly? PaymentDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("PaymentDetails")]
    public virtual string PaymentDetails { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("CreditCard", "DebitCard", "PayPal", "BankTransfer")]
    [Column("PaymentMethod")]
    public virtual PaymentMethodValue? PaymentMethod { get; set; }

    [Relation("orders", "OrderId")]
    public virtual Order Orders { get; }

}