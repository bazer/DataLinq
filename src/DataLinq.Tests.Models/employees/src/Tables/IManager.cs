using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("dept_manager")]
public interface ICustomManager : ICustomTableModel
{
    [Relation("departments", "dept_no")]
    public IDepartment Department { get; }

}