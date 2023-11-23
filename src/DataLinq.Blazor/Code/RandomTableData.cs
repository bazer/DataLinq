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
            () => _db.Query().current_dept_emp.Skip(RandomNumber(0,10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
            () => _db.Query().Departments.Skip(RandomNumber(0,10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
            () => _db.Query().DepartmentEmployees.Skip(RandomNumber(0, 10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
            () => _db.Query().dept_emp_latest_date.Skip(RandomNumber(0, 10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
            () => _db.Query().Managers.Skip(RandomNumber(0, 10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
            () => _db.Query().Employees.Skip(RandomNumber(0, 10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
            () => _db.Query().salaries.Skip(RandomNumber(0, 10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
            () => _db.Query().titles.Skip(RandomNumber(0, 10000)).Take(RandomNumber(1, 10000)).ToList().Cast<object>(),
        };

        var selectedTableIndex = _random.Next(tables.Count);
        return tables[selectedTableIndex]();
    }

    private static int RandomNumber(int min, int max)
    {
        return _random.Next(min, max);
    }
}
