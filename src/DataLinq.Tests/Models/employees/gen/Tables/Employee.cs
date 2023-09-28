﻿using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models
{
    [Table("employees")]
    public partial record Employee : ITableModel<Employees>
    {
        public enum Employeegender
        {
            Empty = 0,
            M = 1,
            F = 2,
        }
    
        [PrimaryKey]
        [AutoIncrement]
        [Type(DatabaseType.MySQL, "int")]
        [Type(DatabaseType.SQLite, "integer")]
        [Column("emp_no")]
        public virtual int? emp_no { get; set; }

        [Type(DatabaseType.MySQL, "date")]
        [Type(DatabaseType.SQLite, "text")]
        [Column("birth_date")]
        public virtual DateOnly birth_date { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 14)]
        [Type(DatabaseType.SQLite, "text")]
        [Column("first_name")]
        public virtual string first_name { get; set; }

        [Type(DatabaseType.MySQL, "enum", 1)]
        [Type(DatabaseType.SQLite, "integer")]
        [Enum("M","F")]
        [Column("gender")]
        public virtual Employeegender? gender { get; set; }

        [Type(DatabaseType.MySQL, "date")]
        [Type(DatabaseType.SQLite, "text")]
        [Column("hire_date")]
        public virtual DateOnly hire_date { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 16)]
        [Type(DatabaseType.SQLite, "text")]
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