using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLinq.Tools.Config
{
    public static class ConfigReader
    {
        public static ConfigFile Read(string path)
        {
            var file = File.ReadAllText(path);
            var config = System.Text.Json.JsonSerializer.Deserialize<ConfigFile>(file);

            return config;
        }
    }
}
