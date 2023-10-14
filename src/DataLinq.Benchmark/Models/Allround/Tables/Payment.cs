﻿using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Benchmark.Models.Allround;

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
    [Type(DatabaseType.MySQL, "decimal")]
    [Column("Amount")]
    public virtual decimal? Amount { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("PaymentDate")]
    public virtual DateOnly? PaymentDate { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum", 12)]
    [Enum()]
    [Column("PaymentMethod")]
    public virtual PaymentMethodValue? PaymentMethod { get; set; }

    [Relation("orders", "OrderId")]
    public virtual Order Orders { get; }

}