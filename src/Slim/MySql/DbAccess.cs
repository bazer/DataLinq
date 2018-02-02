using System;
using System.Collections.Generic;
using System.Text;
using MySql.Data.MySqlClient;

namespace Slim.MySql
{
    public static class DbAccess
    {
        public static string ConnectionString { get; set; }

        static public int ExecuteNonQuery(string query)
        {
            var command = new MySqlCommand(query);

            return ExecuteNonQuery(command);
        }

        static public int ExecuteNonQuery(MySqlCommand command)
        {
            using (var connection = new MySqlConnection(ConnectionString))
            {
                connection.Open();
                command.Connection = connection;
                int result = command.ExecuteNonQuery();
                connection.Close();
                return result;
            }
        }
    }
}
