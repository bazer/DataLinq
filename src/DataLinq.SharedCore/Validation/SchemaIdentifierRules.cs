using System;
using System.Collections.Generic;
using System.Globalization;

namespace DataLinq.Validation;

// Runtime observation only. The public import/CLI comparison defaults remain unchanged.
internal sealed class SchemaIdentifierRules
{
    private SchemaIdentifierRules(DatabaseType databaseType, int? lowerCaseTableNames)
    {
        DatabaseType = databaseType;
        LowerCaseTableNames = lowerCaseTableNames;
        TableNames = databaseType == DatabaseType.SQLite || lowerCaseTableNames is 1 or 2
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    internal DatabaseType DatabaseType { get; }
    internal int? LowerCaseTableNames { get; }
    internal StringComparer TableNames { get; }
    internal StringComparer DatabaseNames => TableNames;
    internal StringComparer ColumnNames => StringComparer.OrdinalIgnoreCase;
    internal StringComparer IndexNames => StringComparer.OrdinalIgnoreCase;
    internal StringComparer ConstraintNames => StringComparer.OrdinalIgnoreCase;
    internal static SchemaIdentifierRules SQLite { get; } = new(DatabaseType.SQLite, null);

    internal static SchemaIdentifierRules Server(DatabaseType databaseType, object? setting)
    {
        if (databaseType is not (DatabaseType.MySQL or DatabaseType.MariaDB))
            throw new ArgumentOutOfRangeException(nameof(databaseType));
        // Do not round numeric values or silently treat missing/boolean values as zero/one.
        var text = setting is string value ? value : setting is byte or sbyte or short or ushort or int or uint or long or ulong
            ? Convert.ToString(setting, CultureInfo.InvariantCulture) : null;
        var mode = text switch { "0" => 0, "1" => 1, "2" => 2, _ => -1 };
        if (mode < 0)
            throw new InvalidOperationException($"{databaseType} returned an unsupported or unreadable @@lower_case_table_names value.");
        return new(databaseType, mode);
    }

    internal static void RequireUnambiguous(IEnumerable<string> names, StringComparer comparer,
        string owner, bool model)
    {
        var seen = new Dictionary<string, string>(comparer);
        foreach (var name in names)
        {
            if (seen.TryGetValue(name, out var previous))
            {
                var message = $"{(model ? "Model" : "Live metadata")} has ambiguous {owner} identifiers '{previous}' and '{name}' under the effective database identifier rules.";
                if (model) throw new ArgumentException(message, "model");
                throw new InvalidOperationException(message);
            }
            seen.Add(name, name);
        }
    }
}
