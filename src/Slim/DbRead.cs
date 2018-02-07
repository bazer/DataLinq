using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MySql.Data.MySqlClient;
using Slim.Interfaces;

namespace Slim
{
    public class DbRead<T> : IEnumerable<T> 
        where T : class, IModel
    {
        //internal ModelReader(string name, IModl m) : base(name, m)
        //{
        //}

        public IEnumerator<T> GetEnumerator() => GetCollection().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        private List<T> GetCollection()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IModel> Sql(MySqlCommand sql)
        {
            throw new NotImplementedException();
        }

        //public IModel New()
        //{
        //    throw new NotImplementedException();
        //}

        public DbRead<T> Get(params object[] id)
        {
            throw new NotImplementedException();
        }
    }
}
