using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [UseCache]
    [Database("employees")]
    public partial interface IEmployees : ICustomDatabaseModel
    {
        DbRead<IDepartment> Departments { get; }
        DbRead<IManager> Managers { get; }
        DbRead<dept_emp> DepartmentEmployees { get; }
        DbRead<IEmployee> Employees { get; }
    }
}