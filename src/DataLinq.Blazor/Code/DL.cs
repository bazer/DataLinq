using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tests.Models;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Blazor.Code;

public static class DL
{
    public static MySqlDatabase<EmployeesDb> Employees { get; set; } = null!;

    public static void Initialize(IConfiguration configuration)
    {
        MySQLProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();

        var connectionString = configuration.GetConnectionString("employees")
            ?? throw new InvalidOperationException("Missing required connection string 'employees'.");

        Employees = new MySqlDatabase<EmployeesDb>(connectionString);
    }
}
