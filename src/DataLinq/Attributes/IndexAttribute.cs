using System;

namespace DataLinq.Attributes;

/// <summary>
/// Represents the underlying data structure or algorithm used by the index.
/// </summary>
public enum IndexType
{
    /// <summary>
    /// Standard B-tree based index common across MySQL, MSSQL, SQLite, and PostgreSQL.
    /// </summary>
    BTREE,

    /// <summary>
    /// Represents full-text search capabilities across MySQL, MSSQL, and SQLite.
    /// </summary>
    FULLTEXT,

    /// <summary>
    /// Represents hash-based index common to MySQL and PostgreSQL.
    /// </summary>
    HASH,

    /// <summary>
    /// Represents spatial index in MySQL.
    /// </summary>
    RTREE,

    /// <summary>
    /// Represents the clustered index in MSSQL, determining the physical order of data.
    /// </summary>
    CLUSTERED,

    /// <summary>
    /// Represents spatial indices across MySQL and MSSQL.
    /// </summary>
    SPATIAL,

    /// <summary>
    /// Represents MSSQL's columnstore index, storing data in a column-wise manner.
    /// </summary>
    COLUMNSTORE,

    /// <summary>
    /// Represents PostgreSQL's general inverted index, used for arrays, full-text search, etc.
    /// </summary>
    GIN,

    /// <summary>
    /// Represents PostgreSQL's generalized search tree.
    /// </summary>
    GIST,

    /// <summary>
    /// Represents PostgreSQL's Block Range INdexes, suitable for large tables with a natural sort order.
    /// </summary>
    BRIN
}

/// <summary>
/// Represents the logical or constraint-related characteristic of the index.
/// </summary>
public enum IndexCharacteristic
{
    /// <summary>
    /// Represents primary key constraint ensuring uniqueness and serving as a main identifier.
    /// </summary>
    PrimaryKey,

    /// <summary>
    /// Represents a foreign key constraint ensuring uniqueness and referencing another table.
    /// </summary>
    ForeignKey,

    /// <summary>
    /// Represents unique constraint ensuring all values in the index are distinct.
    /// </summary>
    Unique,

    /// <summary>
    /// Represents a standard non-unique index.
    /// </summary>
    Simple,

    /// <summary>
    /// Represents MSSQL's filtered index, which can be seen as a partial index.
    /// </summary>
    FILTERED,

    /// <summary>
    /// Represents PostgreSQL's exclusion constraint, ensuring specific non-overlapping properties.
    /// </summary>
    EXCLUSION,

    /// <summary>
    /// Represents an index that only exists internally in Datalinq.
    /// </summary>
    VirtualDataLinq
}

/// <summary>
/// Represents an index attribute for a database column.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class IndexAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IndexAttribute"/> class with the specified name, characteristic, type, and columns.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="characteristic">The characteristic of the index.</param>
    /// <param name="type">The type of the index.</param>
    /// <param name="columns">The columns associated with the index.</param>
    public IndexAttribute(string name, IndexCharacteristic characteristic, IndexType type, params string[] columns)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Index name cannot be empty.", nameof(name));
        //if (columns == null || columns.Length == 0) throw new ArgumentException("An index must have at least one column.", nameof(columns));

        Name = name;
        Type = type;
        Characteristic = characteristic;
        Columns = columns;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexAttribute"/> class with the specified name, characteristic, and columns. The type defaults to BTREE.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="characteristic">The characteristic of the index.</param>
    /// <param name="columns">The columns associated with the index.</param>
    public IndexAttribute(string name, IndexCharacteristic characteristic, params string[] columns)
        : this(name, characteristic, IndexType.BTREE, columns) { }

    /// <summary>
    /// Gets the name of the index.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the type of the index.
    /// </summary>
    public IndexType Type { get; }

    /// <summary>
    /// Gets the characteristic of the index.
    /// </summary>
    public IndexCharacteristic Characteristic { get; }

    /// <summary>
    /// Gets the columns associated with the index.
    /// </summary>
    public string[] Columns { get; }
}