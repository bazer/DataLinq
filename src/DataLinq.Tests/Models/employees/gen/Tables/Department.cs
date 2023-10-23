using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("departments")]
public partial record Department : ITableModel<Employees>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_no")]
    public virtual string DeptNo { get; set; }

    [Index("dept_name", IndexCharacteristic.Unique, IndexType.BTREE)]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_name")]
    public virtual string Name { get; set; }

    [Relation("dept_emp", "dept_no")]
    public virtual IEnumerable<dept_emp> DepartmentEmployees { get; }

    [Relation("dept_manager", "dept_fk")]
    public virtual IEnumerable<Manager> Managers { get; }

}