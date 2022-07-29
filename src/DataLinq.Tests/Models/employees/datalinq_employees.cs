using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [UseCache]
    [Name("datalinq_employees")]
    public interface datalinq_employees : IDatabaseModel
    {
        DbRead<current_dept_emp> current_dept_emp { get; }
        DbRead<departments> departments { get; }
        DbRead<dept_emp> dept_emp { get; }
        DbRead<dept_emp_latest_date> dept_emp_latest_date { get; }
        DbRead<dept_manager> dept_manager { get; }
        DbRead<employees> employees { get; }
        DbRead<salaries> salaries { get; }
        DbRead<titles> titles { get; }
    }
}