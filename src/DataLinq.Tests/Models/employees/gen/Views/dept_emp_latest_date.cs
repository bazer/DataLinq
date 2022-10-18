using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Definition("")]
    [View("dept_emp_latest_date")]
    public partial record dept_emp_latest_date : IViewModel
    {
    }
}