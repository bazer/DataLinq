using DataLinq.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace DataLinq.SQLite
{
    public class SQLiteDatabase<T>: Database<T>
         where T : class, IDatabaseModel
    {
        public SQLiteDatabase(string connectionString) : base(new SQLiteProvider<T>(connectionString))
        {
        }

        public SQLiteDatabase(string connectionString, string databaseName) : base(new SQLiteProvider<T>(connectionString, databaseName))
        {
        }
    }
}
