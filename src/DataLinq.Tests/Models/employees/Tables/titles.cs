using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace Tests.Models
{
    [Name("titles")]
    public partial class titles : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "titles_ibfk_1")]
        [Type("int")]
        public virtual int emp_no { get; set; }

        [Relation("employees", "emp_no")]
        public virtual employees employees { get; }

        [PrimaryKey]
        [Type("date")]
        public virtual DateTime from_date { get; set; }

        [PrimaryKey]
        [Type("varchar", 50)]
        public virtual string title { get; set; }

        [Nullable]
        [Type("date")]
        public virtual DateTime? to_date { get; set; }

    }
}