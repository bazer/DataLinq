using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLinq.MySql
{
    public class MySqlDataLinqDataReader : IDataLinqDataReader
    {
        public MySqlDataLinqDataReader(MySqlDataReader dataReader)
        {
            this.dataReader = dataReader;
        }

        protected MySqlDataReader dataReader;

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
            return dataReader.GetDateOnly(ordinal);
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
