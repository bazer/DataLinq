﻿using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("departments")]
    public partial record departments : ITableModel
    {
        [PrimaryKey]
        [Type("char", 4)]
        [Column("dept_no")]
        public virtual string dept_no { get; set; }

        [Relation("dept_emp", "dept_no")]
        public virtual IEnumerable<dept_emp> dept_emp { get; }

        [Relation("dept_manager", "dept_no")]
        public virtual IEnumerable<dept_manager> dept_manager { get; }

        [Unique("dept_name")]
        [Type("varchar", 40)]
        [Column("dept_name")]
        public virtual string dept_name { get; set; }

    }
}