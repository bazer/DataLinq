using System;
using System.Linq;

namespace DataLinq.Config;

public enum DataLinqModelPropertyOrder
{
    Column,
    Alphabetical
}

public enum DataLinqModelPrimaryKeyPlacement
{
    Top,
    Inline
}

public enum DataLinqModelForeignKeyPlacement
{
    Top,
    Inline
}

public enum DataLinqModelRelationPlacement
{
    Bottom,
    Top,
    WithForeignKey
}

public sealed record DataLinqModelLayoutConfig(
    DataLinqModelPropertyOrder PropertyOrder,
    DataLinqModelPrimaryKeyPlacement PrimaryKeyPlacement,
    DataLinqModelForeignKeyPlacement ForeignKeyPlacement,
    DataLinqModelRelationPlacement RelationPlacement)
{
    public static DataLinqModelLayoutConfig Default { get; } = new(
        DataLinqModelPropertyOrder.Column,
        DataLinqModelPrimaryKeyPlacement.Top,
        DataLinqModelForeignKeyPlacement.Inline,
        DataLinqModelRelationPlacement.Bottom);

    public DataLinqModelLayoutConfig Merge(
        string? propertyOrder,
        string? primaryKeyPlacement,
        string? foreignKeyPlacement,
        string? relationPlacement)
    {
        return new DataLinqModelLayoutConfig(
            Parse(propertyOrder, PropertyOrder, nameof(PropertyOrder)),
            Parse(primaryKeyPlacement, PrimaryKeyPlacement, nameof(PrimaryKeyPlacement)),
            Parse(foreignKeyPlacement, ForeignKeyPlacement, nameof(ForeignKeyPlacement)),
            Parse(relationPlacement, RelationPlacement, nameof(RelationPlacement)));
    }

    private static TEnum Parse<TEnum>(string? value, TEnum currentValue, string propertyName)
        where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(value))
            return currentValue;

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(TEnum), parsed))
        {
            return parsed;
        }

        var allowedValues = string.Join(", ", Enum.GetNames(typeof(TEnum)).OrderBy(static name => name));
        throw new ArgumentException(
            $"Unknown ModelLayout.{propertyName} value '{value}'. Allowed values: {allowedValues}.");
    }
}
