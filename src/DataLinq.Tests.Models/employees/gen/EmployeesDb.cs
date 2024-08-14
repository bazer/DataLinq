using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

[UseCache]
[CacheLimit(CacheLimitType.Megabytes, 200)]
[CacheLimit(CacheLimitType.Minutes, 60)]
[CacheCleanup(CacheCleanupType.Minutes, 30)]
[Database("employees")]
public partial class EmployeesDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<current_dept_emp> current_dept_emp { get; } = new DbRead<current_dept_emp>(dataSource);
    public DbRead<Department> Departments { get; } = new DbRead<Department>(dataSource);
    public DbRead<dept_emp_latest_date> dept_emp_latest_date { get; } = new DbRead<dept_emp_latest_date>(dataSource);
    public DbRead<Manager> Managers { get; } = new DbRead<Manager>(dataSource);
    public DbRead<Dept_emp> DepartmentEmployees { get; } = new DbRead<Dept_emp>(dataSource);
    public DbRead<Employee> Employees { get; } = new DbRead<Employee>(dataSource);
    public DbRead<Salaries> salaries { get; } = new DbRead<Salaries>(dataSource);
    public DbRead<Titles> titles { get; } = new DbRead<Titles>(dataSource);
}