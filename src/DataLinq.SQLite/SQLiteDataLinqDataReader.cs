using Microsoft.Data.Sqlite;
using System;

namespace DataLinq.SQLite
{
    public class SQLiteDataLinqDataReader : IDataLinqDataReader
    {
        public SQLiteDataLinqDataReader(SqliteDataReader dataReader)
        {
            this.dataReader = dataReader;
        }

        protected SqliteDataReader dataReader;

        public void Dispose()
        {
            dataReader.Dispose();
        }

        public string GetString(int ordinal)
        {
            return dataReader.GetString(ordinal);
        }

        public bool GetBoolean(int ordinal)
        {
            return dataReader.GetBoolean(ordinal);
        }

        public int GetInt32(int ordinal)
        {
            return dataReader.GetInt32(ordinal);
        }

        public DateOnly GetDateOnly(int ordinal)
        {
            var date = dataReader.GetDateTime(ordinal);
            return new DateOnly(date.Year, date.Month, date.Day);
        }

        public int GetOrdinal(string name)
        {
            return dataReader.GetOrdinal(name);
        }

        public object GetValue(int ordinal)
        {
            return dataReader.GetValue(ordinal);
        }

        public bool Read()
        {
            return dataReader.Read();
        }

    }
}
