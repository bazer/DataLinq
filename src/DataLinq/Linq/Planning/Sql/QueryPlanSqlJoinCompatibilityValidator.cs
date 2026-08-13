using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DataLinq.Exceptions;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning.Sql;

internal static class QueryPlanSqlJoinCompatibilityValidator
{
    internal static void Validate(QueryPlanTemplate template, DatabaseType databaseType)
    {
        ArgumentNullException.ThrowIfNull(template);

        ValidateOperations(template.Operations, databaseType);
    }

    private static void ValidateOperations(
        IReadOnlyList<QueryPlanOperation> operations,
        DatabaseType databaseType)
    {
        for (var index = 0; index < operations.Count; index++)
        {
            switch (operations[index])
            {
                case QueryPlanOperation.Join join:
                    ValidateJoin(join.JoinShape, databaseType);
                    break;

                case QueryPlanOperation.Pushdown pushdown:
                    ValidateOperations(pushdown.Operations, databaseType);
                    break;
            }
        }
    }

    private static void ValidateJoin(QueryPlanJoin join, DatabaseType databaseType)
    {
        var left = join.LeftColumn;
        var right = join.RightColumn;
        var hasConvertedKey = left.HasScalarConverter || right.HasScalarConverter;
        var hasGuidKey = left.IsGuidColumn || right.IsGuidColumn;

        // Preserve the established primitive join surface. Its broader SQL type
        // compatibility rules belong to schema/provider validation, not this
        // converter- and codec-aware guard.
        if (!hasConvertedKey && !hasGuidKey)
            return;

        if (left.HasScalarConverter != right.HasScalarConverter)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "one key is scalar-converter-backed while the other uses identity mapping");
        }

        var leftProviderType = GetUnderlyingType(left.ProviderClrType);
        var rightProviderType = GetUnderlyingType(right.ProviderClrType);
        if (leftProviderType is null || rightProviderType is null)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the canonical provider CLR type is unresolved on one or both keys");
        }

        if (leftProviderType != rightProviderType)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the canonical provider CLR types differ");
        }

        if (hasConvertedKey)
            ValidateScalarMappings(join, databaseType);

        if (leftProviderType == typeof(Guid))
            ValidateGuidStorage(join, databaseType);
    }

    private static void ValidateScalarMappings(QueryPlanJoin join, DatabaseType databaseType)
    {
        var left = join.LeftColumn;
        var right = join.RightColumn;

        if (!left.ScalarMapping.IsConverterResolved || !right.ScalarMapping.IsConverterResolved)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the scalar converter runtime mapping is unresolved on one or both keys");
        }

        var leftModelType = GetUnderlyingType(left.ModelClrType);
        var rightModelType = GetUnderlyingType(right.ModelClrType);
        if (leftModelType is null || rightModelType is null)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the model CLR type is unresolved on one or both converted keys");
        }

        if (leftModelType != rightModelType)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the converted key model CLR types differ");
        }

        var leftConverterType = left.ScalarMapping.ConverterClrType;
        var rightConverterType = right.ScalarMapping.ConverterClrType;
        if (leftConverterType is null || rightConverterType is null)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the scalar converter CLR type is unresolved on one or both keys");
        }

        if (leftConverterType != rightConverterType)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the scalar converter CLR types differ");
        }
    }

    private static void ValidateGuidStorage(QueryPlanJoin join, DatabaseType databaseType)
    {
        var left = join.LeftColumn;
        var right = join.RightColumn;

        if (left.IsGuidStorageUnresolvedFor(databaseType) ||
            right.IsGuidStorageUnresolvedFor(databaseType))
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the UUID storage format is unresolved for the active provider on one or both keys");
        }

        var leftStorage = left.GetGuidStorageFor(databaseType);
        var rightStorage = right.GetGuidStorageFor(databaseType);
        if (leftStorage is null || rightStorage is null)
        {
            ThrowIncompatible(
                join,
                databaseType,
                "the UUID storage format is missing for the active provider on one or both keys");
        }

        if (leftStorage.Format != rightStorage.Format)
        {
            ThrowIncompatible(
                join,
                databaseType,
                $"the active-provider UUID storage formats differ ({leftStorage.Format} versus {rightStorage.Format})");
        }
    }

    private static Type? GetUnderlyingType(Type? type)
        => type is null ? null : Nullable.GetUnderlyingType(type) ?? type;

    [DoesNotReturn]
    private static void ThrowIncompatible(
        QueryPlanJoin join,
        DatabaseType databaseType,
        string reason)
    {
        throw new QueryTranslationException(
            $"SQL {join.Kind} join key compatibility failed for {join.RightSource.Kind} on provider '{databaseType}': " +
            $"{DescribeColumn(join.LeftColumn, databaseType)} cannot be compared with " +
            $"{DescribeColumn(join.RightColumn, databaseType)} because {reason}.");
    }

    private static string DescribeColumn(ColumnDefinition column, DatabaseType databaseType)
    {
        var providerType = GetUnderlyingType(column.ProviderClrType)?.FullName ?? "<unresolved>";
        var converterType = column.HasScalarConverter
            ? column.ScalarMapping.ConverterClrType?.FullName ?? "<unresolved>"
            : "<identity>";
        var guidStorage = column.IsGuidColumn
            ? column.IsGuidStorageUnresolvedFor(databaseType)
                ? "<unresolved>"
                : column.GetGuidStorageFor(databaseType)?.Format.ToString() ?? "<missing>"
            : "<not-guid>";

        return $"'{column.Table.DbName}.{column.DbName}' " +
               $"[canonical={providerType}, converter={converterType}, uuid={guidStorage}]";
    }
}
