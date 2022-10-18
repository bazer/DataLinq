using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("dept_emp")]
    public interface Idept_emp : ICustomTableModel
    {
      

    }
}