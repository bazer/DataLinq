using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataLinq.Metadata;

namespace DataLinq.Config;

public static class ConfigReader
{
    public static DatabaseType ParseDatabaseType(string? typeName)
    {
        if (typeName == null)
            return DatabaseType.Unknown;

        foreach (var (type, provider) in PluginHook.DatabaseProviders)
        {
            if (provider.IsDatabaseType(typeName))
                return type;
        }

        return DatabaseType.Unknown;

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

    public static ConfigFile? Read(string path)
    {
        var file = File.ReadAllText(path);
        var withoutComments = RemoveComments(file);

        return JsonSerializer.Deserialize(withoutComments, DataLinqConfigJsonContext.Default.ConfigFile);
    }

    private static string RemoveComments(string json)
    {
        var builder = new StringBuilder(json.Length);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var current = json[i];
            var next = i + 1 < json.Length ? json[i + 1] : '\0';

            if (inString)
            {
                builder.Append(current);

                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                builder.Append(current);
                continue;
            }

            if (current == '/' && next == '/')
            {
                i += 2;
                while (i < json.Length && json[i] != '\r' && json[i] != '\n')
                    i++;

                if (i < json.Length)
                    builder.Append(json[i]);

                continue;
            }

            if (current == '/' && next == '*')
            {
                i += 2;
                while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/'))
                    i++;

                if (i + 1 < json.Length)
                    i++;

                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

}

[JsonSerializable(typeof(ConfigFile))]
internal partial class DataLinqConfigJsonContext : JsonSerializerContext;
