using System;
using System.ComponentModel;

namespace DataLinq.Attributes;

/// <summary>
/// Marks provider-imported UUID storage whose binary byte order cannot be
/// inferred from the database schema. This tooling sentinel is emitted beside
/// a blocking source diagnostic and must be replaced by an explicit
/// <see cref="GuidStorageAttribute"/> declaration.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class GuidStorageUnresolvedAttribute : Attribute
{
    /// <summary>Creates an unresolved UUID storage marker for one provider.</summary>
    /// <param name="databaseType">The concrete provider whose byte order is unknown.</param>
    public GuidStorageUnresolvedAttribute(DatabaseType databaseType)
    {
        DatabaseType = databaseType;
    }

    /// <summary>Gets the concrete provider whose UUID byte order is unknown.</summary>
    public DatabaseType DatabaseType { get; }
}
