using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("departments")]
    public interface IDepartment : ICustomTableModel
    {
        [Column("dept_no")]
        public string DeptNo { get; set; }

        [Relation("dept_emp", "dept_no")]
        public IEnumerable<dept_emp> DepartmentEmployees { get; }

        [Relation("dept_manager", "dept_fk")]
        public IEnumerable<Manager> Managers { get; }

        [Column("dept_name")]
        public string Name { get; set; }

        public string ToString()
        {
            return $"Department: {DeptNo}";
        }
    }

    //[Table("departments")]
    //public partial record Department : IDepartment
    //{
    //    [PrimaryKey]
    //    [Type("char", 4)]
    //    [Column("dept_no")]
    //    public virtual string DeptNo { get; set; }

    //    [Relation("dept_emp", "dept_no")]
    //    public virtual IEnumerable<dept_emp> DepartmentEmployees { get; }

    //    [Relation("dept_manager", "dept_no")]
    //    public virtual IEnumerable<dept_manager> Managers { get; }

    //    [Unique("dept_name")]
    //    [Type("varchar", 40)]
    //    [Column("dept_name")]
    //    public virtual string Name { get; set; }

    //}
}