using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("departments")]
public interface IDepartment : ITableModel<IEmployees>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_no")]
    string DeptNo { get; set; }

    [Index("dept_name", IndexCharacteristic.Unique, IndexType.BTREE)]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_name")]
    string Name { get; set; }

    [Relation("dept-emp", "dept_no", "dept_emp_ibfk_2")]
    IEnumerable<IDept_emp> DepartmentEmployees { get; }

    [Relation("dept_manager", "dept_fk", "dept_manager_ibfk_2")]
    IEnumerable<IManager> Managers { get; }

}