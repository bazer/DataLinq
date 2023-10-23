using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models;

[UseCache]
[CacheLimit(CacheLimitType.Gigabytes, 1)]
[CacheCleanup(CacheCleanupType.Minutes, 5)]
[Database("employees")]
public interface Employees : IDatabaseModel
{
    DbRead<current_dept_emp> current_dept_emp { get; }
    DbRead<Department> Departments { get; }
    DbRead<dept_emp> DepartmentEmployees { get; }
    DbRead<dept_emp_latest_date> dept_emp_latest_date { get; }
    DbRead<Manager> Managers { get; }
    DbRead<Employee> Employees { get; }
    DbRead<salaries> salaries { get; }
    DbRead<titles> titles { get; }
}