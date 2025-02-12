using DataLinq.Instances;

namespace DataLinq.Tests.Models.Employees;

public partial interface IDepartmentWithChangedName : IModelInstance<EmployeesDb> { }

public abstract partial class Department
{
    public string NameTest { get; }
}

