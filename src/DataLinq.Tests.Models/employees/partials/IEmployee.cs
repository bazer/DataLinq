namespace DataLinq.Tests.Models.Employees;

public partial class Employee
{
    public string Name => $"{first_name} {last_name}";
}