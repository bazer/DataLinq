using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models
{
    [Table("dept_manager")]
    public interface IManager : ICustomTableModel
    {
        [Relation("departments", "dept_no")]
        public Department Department { get; }

    }
}