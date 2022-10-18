using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Definition("")]
    [View("current_dept_emp")]
    public partial record current_dept_emp : IViewModel
    {
    }
}