using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools;
using DataLinq.Tools.Config;
using Microsoft.Extensions.Configuration;

var configPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json";

var config = ConfigReader.Read(configPath);
//var DbName = args[0];
//var Namespace = args[1];
//var WritePath = Path.GetFullPath(args[2]);

new ModelCreator(Console.WriteLine).Create(config);

//MySqlDatabase<information_schema> GetDatabase()
//{
//    var builder = new ConfigurationBuilder()
//        .SetBasePath(Directory.GetCurrentDirectory())
//        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

//    var configuration = builder.Build();

//    var connectionString = configuration.GetConnectionString("information_schema");
//    return new MySqlDatabase<information_schema>(connectionString);
//}