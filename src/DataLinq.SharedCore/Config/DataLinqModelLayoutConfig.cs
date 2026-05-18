using System;
using System.Linq;

namespace DataLinq.Config;

public enum DataLinqModelPropertyOrder
{
    Column,
    Alphabetical
}

public enum DataLinqModelKeyPlacement
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
    DataLinqModelKeyPlacement KeyPlacement,
    DataLinqModelRelationPlacement RelationPlacement)
{
    public static DataLinqModelLayoutConfig Default { get; } = new(
        DataLinqModelPropertyOrder.Column,
        DataLinqModelKeyPlacement.Top,
        DataLinqModelRelationPlacement.Bottom);

    public DataLinqModelLayoutConfig Merge(
        string? propertyOrder,
        string? keyPlacement,
        string? relationPlacement)
    {
        return new DataLinqModelLayoutConfig(
            Parse(propertyOrder, PropertyOrder, nameof(PropertyOrder)),
            Parse(keyPlacement, KeyPlacement, nameof(KeyPlacement)),
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
