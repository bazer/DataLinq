//using DataLinq.Attributes;
//using DataLinq.Interfaces;

//namespace DataLinq.Tests.Models.Employees;

public enum ManagerType
{
    Unknown,
    Manager,
    AssistantManager,
    FestiveManager
}


//[Table("dept_manager")]
//public interface IManager : ICustomTableModel
//{
//    [Relation("departments", "dept_no")]
//    public IDepartment Department { get; }
//}