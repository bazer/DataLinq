using DataLinq.MySql;
using DataLinq.Tests.Models;

namespace DataLinq.Blazor.Code;
public class RandomTableData
{
    private readonly MySqlDatabase<Employees> _db;
    private static Random _random = new Random();

    public RandomTableData(MySqlDatabase<Employees> db)
    {
        _db = db;
    }

    public IEnumerable<object> GetRandomData()
    {
        var tables = new List<Func<IEnumerable<object>>>
        {
            () => _db.Query().current_dept_emp.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
            () => _db.Query().Departments.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
            () => _db.Query().DepartmentEmployees.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
            () => _db.Query().dept_emp_latest_date.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
            () => _db.Query().Managers.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
            () => _db.Query().Employees.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
            () => _db.Query().salaries.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
            () => _db.Query().titles.Take(RandomNumber(1, 10)).ToList().Cast<object>(),
        };

        var selectedTableIndex = _random.Next(tables.Count);
        return tables[selectedTableIndex]();
    }

    private static int RandomNumber(int min, int max)
    {
        return _random.Next(min, max);
    }
}
