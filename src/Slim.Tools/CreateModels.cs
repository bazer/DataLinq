using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;
using Slim.MySql;
using Slim.Extensions;
using Slim.Metadata;

namespace Slim.Tools
{
    class CreateModels
    {
         public void Execute(string dbname, string namespaceName, string path)
        {
            var database = MetadataFromSqlFactory.ParseDatabase(dbname);

            Console.WriteLine($"Database: {dbname}");
            Console.WriteLine($"Table count: {database.Tables.Count}");
            Console.WriteLine($"Writing models to: {path}");

            foreach (var file in FileFactory.CreateModelFiles(database, namespaceName))
            {
                var filepath = $"{path}{Path.DirectorySeparatorChar}{file.path}";
                Console.WriteLine($"Writing {filepath}");

                if (!File.Exists(filepath))
                    Directory.CreateDirectory(Path.GetDirectoryName(filepath));

                File.WriteAllText(filepath, file.contents, Encoding.GetEncoding("iso-8859-1"));
            }
        }
    }
}
