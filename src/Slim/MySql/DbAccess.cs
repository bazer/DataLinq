using System;
using System.Collections.Generic;
using System.Data;
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

        static public MySqlDataReader ExecuteReader(MySqlCommand command)
        {
            var connection = new MySqlConnection(ConnectionString);
            command.Connection = connection;
            connection.Open();

            return command.ExecuteReader(CommandBehavior.CloseConnection);
        }

        static public IEnumerable<MySqlDataReader> ReadReader(this MySqlCommand command)
        {
            using (var reader = ExecuteReader(command))
                while (reader.Read())
                    yield return reader;
        }
    }
}
