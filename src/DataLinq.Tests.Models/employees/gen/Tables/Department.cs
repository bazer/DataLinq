using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

public partial interface IDepartmentWithChangedName
{
}

[Table("departments")]
[Interface<IDepartmentWithChangedName>]
public abstract partial class Department(IRowData rowData, IDataSourceAccess dataSource) : Immutable<Department, EmployeesDb>(rowData, dataSource), ITableModel<EmployeesDb>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_no")]
    public abstract string DeptNo { get; }

    [Index("dept_name", IndexCharacteristic.Unique, IndexType.BTREE)]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_name")]
    public abstract string Name { get; }

    [Relation("dept-emp", "dept_no", "dept_emp_ibfk_2")]
    public abstract IImmutableRelation<Dept_emp> DepartmentEmployees { get; }

    [Relation("dept_manager", "dept_fk", "dept_manager_ibfk_2")]
    public abstract IImmutableRelation<Manager> Managers { get; }

}