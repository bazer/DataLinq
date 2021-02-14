using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("employees")]
    public partial class employees : ITableModel
    {
        [PrimaryKey]
        [AutoIncrement]
        [Type("int")]
        public virtual int? emp_no { get; set; }

        [Relation("dept_emp", "emp_no")]
        public virtual IEnumerable<dept_emp> dept_emp { get; }

        [Relation("dept_manager", "emp_no")]
        public virtual IEnumerable<dept_manager> dept_manager { get; }

        [Relation("salaries", "emp_no")]
        public virtual IEnumerable<salaries> salaries { get; }

        [Relation("titles", "emp_no")]
        public virtual IEnumerable<titles> titles { get; }

        [Type("date")]
        public virtual DateTime birth_date { get; set; }

        [Type("varchar", 14)]
        public virtual string first_name { get; set; }

        [Type("enum", 1)]
        public virtual int gender { get; set; }

        [Type("date")]
        public virtual DateTime hire_date { get; set; }

        [Type("varchar", 16)]
        public virtual string last_name { get; set; }

    }
}