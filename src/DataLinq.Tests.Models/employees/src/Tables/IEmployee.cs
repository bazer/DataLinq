using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("employees")]
public interface ICustomEmployee : ICustomTableModel
{
}