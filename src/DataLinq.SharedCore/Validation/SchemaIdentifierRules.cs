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
        TableNames = databaseType == DatabaseType.SQLite ? new SQLiteNameComparer()
            : lowerCaseTableNames is 1 or 2 ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    internal DatabaseType DatabaseType { get; }
    internal int? LowerCaseTableNames { get; }
    internal StringComparer TableNames { get; }
    internal StringComparer DatabaseNames => TableNames;
    internal StringComparer ColumnNames => DatabaseType == DatabaseType.SQLite ? TableNames : StringComparer.OrdinalIgnoreCase;
    internal StringComparer IndexNames => ColumnNames;
    internal StringComparer ConstraintNames => ColumnNames;
    internal static SchemaIdentifierRules SQLite { get; } = new(DatabaseType.SQLite, null);

    // SQLite's sqlite3_stricmp folds only ASCII A-Z in identifiers. Unicode
    // case folding would merge valid distinct names such as årows and Årows.
    private sealed class SQLiteNameComparer : StringComparer
    {
        private static char Fold(char value) => value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;

        public override int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            for (var index = 0; index < Math.Min(x.Length, y.Length); index++)
            {
                var difference = Fold(x[index]) - Fold(y[index]);
                if (difference != 0) return difference;
            }
            return x.Length.CompareTo(y.Length);
        }

        public override bool Equals(string? x, string? y) => Compare(x, y) == 0;

        public override int GetHashCode(string obj)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            var hash = 17;
            foreach (var character in obj) hash = unchecked(hash * 31 + Fold(character));
            return hash;
        }
    }

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
