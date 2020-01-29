using Slim.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.MySql
{
    public class MySqlDatabase<T>: Database<T>
         where T : class, IDatabaseModel
    {
        public MySqlDatabase(string connectionString) : base(new MySQLProvider<T>(connectionString))
        {
        }

        public MySqlDatabase(string connectionString, string databaseName) : base(new MySQLProvider<T>(connectionString, databaseName))
        {
        }
    }
}
