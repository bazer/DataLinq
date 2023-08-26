﻿using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models
{
    [Table("dept_manager")]
    public partial record Manager : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("departments", "dept_no", "dept_manager_ibfk_2")]
        [Type(DatabaseType.SQLite, "text")]
        [Type(DatabaseType.MySQL, "char", 4)]
        [Column("dept_fk")]
        public virtual string dept_fk { get; set; }

        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "dept_manager_ibfk_1")]
        [Type(DatabaseType.SQLite, "integer")]
        [Type(DatabaseType.MySQL, "int")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [Type(DatabaseType.SQLite, "text")]
        [Type(DatabaseType.MySQL, "date")]
        [Column("from_date")]
        public virtual DateOnly from_date { get; set; }

        [Type(DatabaseType.SQLite, "text")]
        [Type(DatabaseType.MySQL, "date")]
        [Column("to_date")]
        public virtual DateOnly to_date { get; set; }

        [Relation("departments", "dept_no")]
        public virtual Department Department { get; }

        [Relation("employees", "emp_no")]
        public virtual Employee employees { get; }

    }
}