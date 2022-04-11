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

        public DateOnly GetDateOnly(int ordinal)
        {
            throw new NotImplementedException();
            //return dataReader.GetDateOnly(ordinal);
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
