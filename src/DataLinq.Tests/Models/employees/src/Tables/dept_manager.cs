using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("dept_manager")]
    public interface Idept_manager : ICustomTableModel
    {
        

    }
}