using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using MySql.Data.MySqlClient;
using Slim.Mutation;

namespace Slim.MySql
{
    public class MySqlDbAccess : DatabaseTransaction
    {
        public MySqlDbAccess(string connectionString, TransactionType type) : base(connectionString, type)
        {
            if (type != TransactionType.NoTransaction)
                throw new ArgumentException("Only 'TransactionType.NoTransaction' is allowed");
        }

        public override void Commit()
        {
            
        }

        public override void Dispose()
        {
            
        }

        public override int ExecuteNonQuery(IDbCommand command)
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

        public override int ExecuteNonQuery(string query) => 
            ExecuteNonQuery(new MySqlCommand(query));

        public override DbDataReader ExecuteReader(IDbCommand command)
        {
            var connection = new MySqlConnection(ConnectionString);
            command.Connection = connection;
            connection.Open();

            return command.ExecuteReader(CommandBehavior.CloseConnection) as DbDataReader;
        }

        public override DbDataReader ExecuteReader(string query) => 
            ExecuteReader(new MySqlCommand(query));

        public override void Rollback()
        {
            
        }
    }
}
