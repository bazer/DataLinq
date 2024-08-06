using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models;

[UseCache]
[CacheLimit(CacheLimitType.Megabytes, 200)]
[CacheLimit(CacheLimitType.Minutes, 60)]
[CacheCleanup(CacheCleanupType.Minutes, 30)]
[Database("employees")]
public interface IEmployees : IDatabaseModel
{
    DbRead<ICurrent_dept_emp> current_dept_emp { get; }
    DbRead<IDepartment> Departments { get; }
    DbRead<IDept_emp_latest_date> dept_emp_latest_date { get; }
    DbRead<IManager> Managers { get; }
    DbRead<IDept_emp> DepartmentEmployees { get; }
    DbRead<IEmployee> Employees { get; }
    DbRead<ISalaries> salaries { get; }
    DbRead<ITitles> titles { get; }
}