using System;
using System.IO;
using System.Text.Json;
using DataLinq.Config;

namespace DataLinq.CLI;

internal static class CliConfigLoader
{
    public static bool TryRead(
        string configPath,
        Action<string> log,
        out DataLinqConfig config,
        out object failure)
    {
        try
        {
            var read = DataLinqConfig.FindAndReadConfigs(configPath, log);
            if (read.HasFailed)
            {
                config = null!;
                failure = read.Failure;
                return false;
            }

            config = read.Value;
            failure = null!;
            return true;
        }
        catch (JsonException exception)
        {
            config = null!;
            failure = $"Couldn't parse config file '{configPath}'. {exception.Message}";
            return false;
        }
        catch (IOException exception)
        {
            config = null!;
            failure = $"Couldn't read config file '{configPath}'. {exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            config = null!;
            failure = $"Couldn't read config file '{configPath}'. {exception.Message}";
            return false;
        }
        catch (ArgumentException exception)
        {
            config = null!;
            failure = exception.Message;
            return false;
        }
    }
}
