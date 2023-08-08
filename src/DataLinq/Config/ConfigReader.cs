using DataLinq.Metadata;
using System;
using System.IO;
using System.Text;

namespace DataLinq.Config
{
    public static class ConfigReader
    {
        public static DatabaseType? ParseDatabaseType(string? typeName)
        {
            if (typeName == null)
                return null;

            foreach (var (type, provider) in PluginHook.DatabaseProviders)
            {
                if (provider.IsDatabaseType(typeName))
                    return type;
            }

            return null;

           // return $"No provider matched with database type '{typeName}'";
        }

        public static Encoding ParseFileEncoding(string encoding)
        {
            if (encoding == null || encoding.Equals("UTF8", StringComparison.OrdinalIgnoreCase) || encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                return new UTF8Encoding(false);

            if (encoding.Equals("UTF8BOM", StringComparison.OrdinalIgnoreCase) || encoding.Equals("UTF8-BOM", StringComparison.OrdinalIgnoreCase) || encoding.Equals("UTF-8-BOM", StringComparison.OrdinalIgnoreCase))
                return new UTF8Encoding(true);

            return Encoding.GetEncoding(encoding);
        }

        public static ConfigFile Read(string path)
        {
            var file = File.ReadAllText(path);
            var config = System.Text.Json.JsonSerializer.Deserialize<ConfigFile>(file);

            return config;
        }
    }
}
