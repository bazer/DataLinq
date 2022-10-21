using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("dept_manager")]
    public interface IManager : ICustomTableModel
    {
        [Relation("departments", "dept_no")]
        public Department Department { get; }

    }
}