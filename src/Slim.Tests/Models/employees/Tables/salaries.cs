using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("salaries")]
    public partial class salaries : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "salaries_ibfk_1")]
        [Type("int")]
        public virtual int emp_no { get; set; }

        [Relation("employees", "emp_no")]
        public virtual employees employees { get; }

        [PrimaryKey]
        [Type("date")]
        public virtual DateTime from_date { get; set; }

        [Type("int")]
        public virtual int salary { get; set; }

        [Type("date")]
        public virtual DateTime to_date { get; set; }

    }
}