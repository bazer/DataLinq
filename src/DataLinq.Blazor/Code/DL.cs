using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tests.Models;

namespace DataLinq.Blazor.Code
{
    public static class DL
    {
        public static MySqlDatabase<Employees> Employees { get; set; }

        public static void Initialize(IConfiguration configuration)
        {
            MySQLProvider.RegisterProvider();
            SQLiteProvider.RegisterProvider();
            Employees = new MySqlDatabase<Employees>(configuration.GetConnectionString("employees"));
        }
    }
}
