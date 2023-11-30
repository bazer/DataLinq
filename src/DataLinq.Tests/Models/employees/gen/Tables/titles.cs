using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("titles")]
public partial record titles : ITableModel<Employees>
{
    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "titles_ibfk_1")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    public virtual int emp_no { get; set; }

    [PrimaryKey]
    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    public virtual DateOnly from_date { get; set; }

    [PrimaryKey]
    [Type(DatabaseType.MySQL, "varchar", 50)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("title")]
    public virtual string title { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    public virtual DateOnly? to_date { get; set; }

    [Relation("employees", "emp_no")]
    public virtual Employee employees { get; }

}