using Castle.Components.DictionaryAdapter.Xml;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace DataLinq.Config
{
    /// <summary>
    /// Represents a connection string and provides methods to access its components.
    /// </summary>
    public record DataLinqConnectionString
    {
        /// <summary>
        /// Gets the dictionary containing the key-value pairs of the connection string.
        /// </summary>
        public Dictionary<string, string> Entries { get; }

        /// <summary>
        /// Gets the original connection string.
        /// </summary>
        public string Original { get; }

        /// <summary>
        /// Gets a collection of key-value pairs from the connection string.
        /// </summary>
        public IEnumerable<(string key, string value)> Values => Entries.Select(x => (x.Key, x.Value));

        /// <summary>
        /// Determines whether the connection string contains the specified key.
        /// </summary>
        /// <param name="key">The key to locate in the connection string.</param>
        /// <returns>true if the connection string contains an element with the specified key; otherwise, false.</returns>
        public bool ContainsKey(string key) => Entries.ContainsKey(key.ToLower());

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>The value associated with the specified key.</returns>
        public string? GetValue(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            Entries.TryGetValue(key.ToLower(), out var value);
            return value;
        }

        /// <summary>
        /// Determines whether the connection string contains a password entry.
        /// </summary>
        public bool HasPassword =>
            ContainsKey("password") ||
            ContainsKey("pwd");

        /// <summary>
        /// Gets the path (either 'host' or 'data source') from the connection string.
        /// </summary>
        public string? Path
        {
            get
            {
                if (ContainsKey("host"))
                    return GetValue("host");

                if (ContainsKey("data source"))
                    return GetValue("data source");

                return null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataLinqConnectionString"/> class with the specified connection string.
        /// </summary>
        /// <param name="original">The original connection string.</param>
        public DataLinqConnectionString(string? original)
        {
            Original = original ?? throw new ArgumentNullException(nameof(original));

            var builder = new DbConnectionStringBuilder();
            builder.ConnectionString = Original;

            Entries = builder.Keys
                .Cast<string>()
                .Select(x => (x.ToLowerInvariant(), builder[x].ToString()))
                .Where(x => x.Item2 != null)
                .ToDictionary(x => x.Item1, x => x.Item2 ?? "");
        }

        public DataLinqConnectionString ChangeValue(string key, string value)
        {
            var builder = new DbConnectionStringBuilder
            {
                { key, value }
            };

            foreach (var entry in Entries)
                if (entry.Key != key)
                    builder.Add(entry.Key, entry.Value);

            return new DataLinqConnectionString(builder.ToString());
        }
    }
}
