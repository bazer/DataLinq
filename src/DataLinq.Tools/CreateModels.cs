using System;
using System.IO;
using System.Text;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;

namespace DataLinq.Tools
{
    internal class CreateModels
    {
        public void Execute(string dbname, string namespaceName, string path, MySqlDatabase<information_schema> information_schema)
        {
            var database = MetadataFromSqlFactory.ParseDatabase(dbname, information_schema.Query());

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