using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models.Employees;

[UseCache]
[CacheLimit(CacheLimitType.Megabytes, 200)]
[CacheLimit(CacheLimitType.Minutes, 60)]
[CacheCleanup(CacheCleanupType.Minutes, 30)]
[Database("employees")]
public partial class EmployeesDb(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<current_dept_emp> current_dept_emp { get; } = new(readSource);
    public DbRead<Department> Departments { get; } = new(readSource);
    public DbRead<dept_emp_latest_date> dept_emp_latest_date { get; } = new(readSource);
    public DbRead<Manager> Managers { get; } = new(readSource);
    public DbRead<Dept_emp> DepartmentEmployees { get; } = new(readSource);
    public DbRead<Employee> Employees { get; } = new(readSource);
    public DbRead<Salaries> salaries { get; } = new(readSource);
    public DbRead<Titles> titles { get; } = new(readSource);
}
