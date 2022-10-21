using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [UseCache]
    [Database("employees")]
    public interface employeesDb : IDatabaseModel
    {
        DbRead<current_dept_emp> current_dept_emp { get; }
        DbRead<Department> Departments { get; }
        DbRead<dept_emp> DepartmentEmployees { get; }
        DbRead<dept_emp_latest_date> dept_emp_latest_date { get; }
        DbRead<Manager> Managers { get; }
        DbRead<employees> employees { get; }
        DbRead<salaries> salaries { get; }
        DbRead<titles> titles { get; }
    }
}