using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("employees")]
    public partial record employees : ITableModel
    {
        [PrimaryKey]
        [AutoIncrement]
        [Type(DatabaseType.MySQL, "int")]
        [Column("emp_no")]
        public virtual int? emp_no { get; set; }

        [Type(DatabaseType.MySQL, "date")]
        [Column("birth_date")]
        public virtual DateOnly birth_date { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 14)]
        [Column("first_name")]
        public virtual string first_name { get; set; }

        [Type(DatabaseType.MySQL, "int")]
        [Column("gender")]
        public virtual int gender { get; set; }

        [Type(DatabaseType.MySQL, "date")]
        [Column("hire_date")]
        public virtual DateOnly hire_date { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 16)]
        [Column("last_name")]
        public virtual string last_name { get; set; }

        [Relation("dept_emp", "emp_no")]
        public virtual IEnumerable<dept_emp> dept_emp { get; }

        [Relation("dept_manager", "emp_no")]
        public virtual IEnumerable<Manager> dept_manager { get; }

        [Relation("salaries", "emp_no")]
        public virtual IEnumerable<salaries> salaries { get; }

        [Relation("titles", "emp_no")]
        public virtual IEnumerable<titles> titles { get; }

    }
}