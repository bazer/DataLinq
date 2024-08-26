using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.Employees;

[Table("employees")]
public interface ICustomEmployee : ICustomTableModel
{
}