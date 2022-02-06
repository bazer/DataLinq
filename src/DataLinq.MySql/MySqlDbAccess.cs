﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using MySqlConnector;
using DataLinq.Mutation;

namespace DataLinq.MySql
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

        public override object ExecuteScalar(IDbCommand command)
        {
            using (var connection = new MySqlConnection(ConnectionString))
            {
                connection.Open();
                command.Connection = connection;
                object result = command.ExecuteScalar();
                connection.Close();

                return result;
            }
        }

        public override IDataLinqDataReader ExecuteReader(IDbCommand command)
        {
            var connection = new MySqlConnection(ConnectionString);
            command.Connection = connection;
            connection.Open();

            return new MySqlDataLinqDataReader(command.ExecuteReader(CommandBehavior.CloseConnection) as MySqlDataReader);
        }

        public override IDataLinqDataReader ExecuteReader(string query) => 
            ExecuteReader(new MySqlCommand(query));

        public override void Rollback()
        {
            
        }
    }
}
