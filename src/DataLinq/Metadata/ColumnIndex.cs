using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;

namespace DataLinq.Metadata;

/// <summary>
/// Represents an index associated with one or more columns in a database table.
/// </summary>
public class ColumnIndex
{
    private List<Column> columns;
    /// <summary>
    /// Gets or sets the list of columns associated with the index. 
    /// Each entry includes the column in the order of the index.
    /// </summary>
    public IEnumerable<Column> Columns => columns;

    /// <summary>
    /// Gets or sets the characteristic of the index, such as whether it's a primary key or unique.
    /// </summary>
    public IndexCharacteristic Characteristic { get; }

    /// <summary>
    /// Gets or sets the type of the index, indicating the underlying data structure or algorithm used.
    /// </summary>
    public IndexType Type { get; }

    /// <summary>
    /// Gets or sets the name of the index.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ColumnIndex"/> class.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="characteristic">The characteristic of the index.</param>
    /// <param name="type">The type of the index.</param>
    /// <param name="columns">The columns associated with the index.</param>
    public ColumnIndex(string name, IndexCharacteristic characteristic, IndexType type, List<Column> columns)
    {
        Name = name;
        Characteristic = characteristic;
        Type = type;
        this.columns = columns ?? new List<Column>();
        Validate();
    }

    public void AddColumn(Column column)
    {
        if (Columns.Contains(column))
            throw new ArgumentException($"Columns already contains column '{column}'");

        columns.Add(column);
    }

    /// <summary>
    /// Provides a string representation of the <see cref="ColumnIndex"/> object.
    /// </summary>
    /// <returns>A string representation of the index.</returns>
    public override string ToString()
    {
        return $"{Name} ({Type}, {Characteristic}) on columns: {string.Join(", ", Columns.Select(c => c.DbName))}";
    }

    /// <summary>
    /// Validates the index's type and characteristic.
    /// </summary>
    private void Validate()
    {
        // Index name should not be empty.
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Index name cannot be empty.");
        }

        // An index should have at least one column.
        if (!Columns.Any())
        {
            throw new InvalidOperationException("An index should have at least one column.");
        }

        // A FULLTEXT index should not be a primary key or unique.
        if (Type == IndexType.FULLTEXT && (Characteristic == IndexCharacteristic.PrimaryKey || Characteristic == IndexCharacteristic.Unique))
        {
            throw new InvalidOperationException("A FULLTEXT index cannot be a primary key or unique.");
        }

        // A primary key index should not be of type FULLTEXT or HASH.
        if (Characteristic == IndexCharacteristic.PrimaryKey && (Type == IndexType.FULLTEXT || Type == IndexType.HASH))
        {
            throw new InvalidOperationException("A primary key index cannot be of type FULLTEXT or HASH.");
        }

        // A unique index should not be of type FULLTEXT.
        if (Characteristic == IndexCharacteristic.Unique && Type == IndexType.FULLTEXT)
        {
            throw new InvalidOperationException("A unique index cannot be of type FULLTEXT.");
        }

        // A FULLTEXT index should have Simple as its characteristic.
        if (Type == IndexType.FULLTEXT && Characteristic != IndexCharacteristic.Simple)
        {
            throw new InvalidOperationException("A FULLTEXT index should have Simple as its characteristic.");
        }

        // Additional validations can be added here as needed.
    }
}
