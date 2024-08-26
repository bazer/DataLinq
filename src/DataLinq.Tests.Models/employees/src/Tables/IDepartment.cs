using System.Collections.Generic;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Employees;

[Table("departments")]
public interface ICustomDepartment : ICustomTableModel
{
    [Column("dept_no")]
    public string DeptNo { get; set; }

    [Relation("dept-emp", "dept_no")]
    public IEnumerable<Dept_emp> DepartmentEmployees { get; }

    [Relation("dept_manager", "dept_fk")]
    public IEnumerable<ICustomManager> Managers { get; }

    [Column("dept_name")]
    public string Name { get; set; }

    public string ToString()
    {
        return $"Department: {DeptNo}";
    }
}