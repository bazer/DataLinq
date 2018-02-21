using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Modl.Db;
using Modl.Db.Query;

namespace Slim.MySql
{
	public static class DbAccess
	{
        static public bool ExecuteNonQuery(params IQuery[] queries)
		{
            return ExecuteNonQuery(new List<IQuery>(queries));
		}

        static public bool ExecuteNonQuery(List<IQuery> queries)
		{
			return ExecuteNonQuery(DatabaseProvider.GetDbCommands(queries));
		}

		static public bool ExecuteNonQuery(List<IDbCommand> commands)
		{
			for (int i = 0; i < commands.Count; i++)
			{
				if (commands[i].Connection.State != ConnectionState.Open)
					commands[i].Connection.Open();

				commands[i].ExecuteNonQuery();

                if (i + 1 == commands.Count || commands[i].Connection != commands[i + 1].Connection)
                    commands[i].Connection.Close();
			}

            return true;
		}

		static public object ExecuteScalar(Type type, params IQuery[] queries)
		{
			return ExecuteScalar(type, new List<IQuery>(queries));
		}

        static public object ExecuteScalar(Type type, List<IQuery> queries)
		{
            return ExecuteScalar(type, DatabaseProvider.GetDbCommands(queries));
		}

        static public object ExecuteScalar(Type type, List<IDbCommand> commands)
		{
			//T result = default(T);
            object result = null;
			
			for (int i = 0; i < commands.Count; i++)
			{
				if (commands[i].Connection.State != ConnectionState.Open)
					commands[i].Connection.Open();

				object o = commands[i].ExecuteScalar();

                if (i + 1 == commands.Count || commands[i].Connection != commands[i + 1].Connection)
                    commands[i].Connection.Close();

				if (o != null && o != DBNull.Value)
					result = Convert.ChangeType(o, type);
			}

			return result;
		}

        static public IEnumerable<DbDataReader> ExecuteReader(params IQuery[] queries)
        {
            return ExecuteReader(new List<IQuery>(queries));
        }

        static public IEnumerable<DbDataReader> ExecuteReader(List<IQuery> queries)
        {
            return ExecuteReader(DatabaseProvider.GetDbCommands(queries));
        }

        static public IEnumerable<DbDataReader> ExecuteReader(List<IDbCommand> commands)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                if (commands[i].Connection.State != ConnectionState.Open)
                    commands[i].Connection.Open();

                yield return (DbDataReader)commands[i].ExecuteReader(CommandBehavior.CloseConnection);
            }
        }

        //static public IEnumerable<DbDataReader> ReadReader(this IDbCommand command)
        //{
        //    using (var reader = ExecuteReader(command))
        //        while (reader.Read())
        //            yield return reader;
        //}



        //static public DbDataReader ExecuteReader(IDbCommand command)
        //{
        //    if (command.Connection.State != ConnectionState.Open)
        //        command.Connection.Open();

        //    //return (DbDataReader)command.ExecuteReader();
        //    return (DbDataReader)command.ExecuteReader(CommandBehavior.CloseConnection);
        //}
    }
}
