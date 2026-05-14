using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.ErrorHandling;

public enum DataLinqDiagnosticSeverity
{
    Error,
    Warning
}

public sealed class DataLinqDiagnosticIssue
{
    public DataLinqDiagnosticIssue(
        DataLinqDiagnosticSeverity severity,
        DLFailureType failureType,
        string message,
        SourceLocation? sourceLocation = null,
        string? objectPath = null,
        IReadOnlyList<string>? contextMessages = null)
    {
        Severity = severity;
        FailureType = failureType;
        Message = message ?? "";
        SourceLocation = sourceLocation;
        ObjectPath = objectPath;
        ContextMessages = contextMessages ?? [];
    }

    public DataLinqDiagnosticSeverity Severity { get; }
    public DLFailureType FailureType { get; }
    public string Message { get; }
    public SourceLocation? SourceLocation { get; }
    public string? ObjectPath { get; }
    public IReadOnlyList<string> ContextMessages { get; }

    public string FormatLocation(string? sourceText = null)
    {
        if (SourceLocation.HasValue)
            return SourceLocationFormatter.Format(SourceLocation.Value, sourceText);

        return ObjectPath ?? "";
    }

    public static IReadOnlyList<DataLinqDiagnosticIssue> FromFailure(IDLOptionFailure failure)
    {
        if (failure == null)
            throw new ArgumentNullException(nameof(failure));

        var issues = new List<DataLinqDiagnosticIssue>();
        AddIssues(failure, [], null, null, issues);
        issues.Sort(DataLinqDiagnosticIssueComparer.Instance);
        return issues;
    }

    private static void AddIssues(
        IDLOptionFailure failure,
        IReadOnlyList<string> parentContextMessages,
        SourceLocation? parentSourceLocation,
        string? parentObjectPath,
        List<DataLinqDiagnosticIssue> issues)
    {
        var failureContext = GetFailureContextMessage(failure);
        var contextMessages = parentContextMessages;
        if (failure.HasInnerFailures &&
            !string.IsNullOrWhiteSpace(failureContext))
            contextMessages = [.. parentContextMessages, failureContext];

        var sourceLocation = failure.SourceLocation ?? parentSourceLocation;
        var objectPath = GetObjectPath(failure) ?? parentObjectPath;

        if (failure.HasInnerFailures)
        {
            foreach (var innerFailure in failure.InnerFailures)
                AddIssues(innerFailure, contextMessages, sourceLocation, objectPath, issues);

            return;
        }

        issues.Add(new DataLinqDiagnosticIssue(
            DataLinqDiagnosticSeverity.Error,
            failure.FailureType,
            GetFailureMessage(failure),
            sourceLocation,
            objectPath,
            contextMessages));
    }

    private static string GetFailureContextMessage(IDLOptionFailure failure)
    {
        if (failure.FailureValue is IFailureWithDefinition definitionFailure)
            return definitionFailure.FailureValue?.ToString() ?? "";

        return failure.OwnMessage;
    }

    private static string GetFailureMessage(IDLOptionFailure failure)
    {
        if (failure.FailureValue is IFailureWithDefinition definitionFailure)
            return definitionFailure.FailureValue?.ToString() ?? "";

        return failure.OwnMessage;
    }

    private static string? GetObjectPath(IDLOptionFailure failure)
    {
        if (failure.FailureValue is IFailureWithDefinition definitionFailure)
            return DataLinqDiagnosticObjectPath.GetObjectPath(definitionFailure.Definition);

        return null;
    }
}

public static class DataLinqDiagnosticObjectPath
{
    public static string? GetObjectPath(IDefinition? definition)
    {
        if (definition == null)
            return null;

        return definition switch
        {
            ColumnDefinition column => GetColumnPath(column),
            ColumnIndex columnIndex => GetColumnIndexPath(columnIndex),
            TableDefinition table => GetTablePath(table),
            PropertyDefinition property => GetPropertyPath(property),
            ModelDefinition model => $"model:{FormatCsType(model.CsType)}",
            DatabaseDefinition database => $"database:{database.Name}",
            _ => definition.ToString()
        };
    }

    private static string GetColumnPath(ColumnDefinition column)
    {
        var tablePath = GetTablePath(column.Table);
        return string.IsNullOrWhiteSpace(tablePath)
            ? $"column:{column.DbName}"
            : $"{tablePath}.column:{column.DbName}";
    }

    private static string GetColumnIndexPath(ColumnIndex columnIndex)
    {
        var tablePath = GetTablePath(columnIndex.Table);
        return string.IsNullOrWhiteSpace(tablePath)
            ? $"index:{columnIndex.Name}"
            : $"{tablePath}.index:{columnIndex.Name}";
    }

    private static string GetTablePath(TableDefinition table)
    {
        try
        {
            return $"database:{table.Database.Name}.table:{table.DbName}";
        }
        catch (InvalidOperationException)
        {
            return $"table:{table.DbName}";
        }
        catch (NullReferenceException)
        {
            return $"table:{table.DbName}";
        }
    }

    private static string GetPropertyPath(PropertyDefinition property)
    {
        var modelPath = property.Model == null
            ? null
            : $"model:{FormatCsType(property.Model.CsType)}";

        return string.IsNullOrWhiteSpace(modelPath)
            ? $"property:{property.PropertyName}"
            : $"{modelPath}.property:{property.PropertyName}";
    }

    private static string FormatCsType(CsTypeDeclaration csType)
    {
        return string.IsNullOrWhiteSpace(csType.Namespace)
            ? csType.Name
            : $"{csType.Namespace}.{csType.Name}";
    }
}

internal sealed class DataLinqDiagnosticIssueComparer : IComparer<DataLinqDiagnosticIssue>
{
    public static DataLinqDiagnosticIssueComparer Instance { get; } = new();

    public int Compare(DataLinqDiagnosticIssue? x, DataLinqDiagnosticIssue? y)
    {
        if (ReferenceEquals(x, y))
            return 0;

        if (x is null)
            return -1;

        if (y is null)
            return 1;

        var locationComparison = CompareSourceLocations(x.SourceLocation, y.SourceLocation);
        if (locationComparison != 0)
            return locationComparison;

        var objectPathComparison = string.Compare(x.ObjectPath, y.ObjectPath, StringComparison.Ordinal);
        if (objectPathComparison != 0)
            return objectPathComparison;

        var typeComparison = x.FailureType.CompareTo(y.FailureType);
        if (typeComparison != 0)
            return typeComparison;

        return string.Compare(x.Message, y.Message, StringComparison.Ordinal);
    }

    private static int CompareSourceLocations(SourceLocation? x, SourceLocation? y)
    {
        if (!x.HasValue && !y.HasValue)
            return 0;

        if (!x.HasValue)
            return 1;

        if (!y.HasValue)
            return -1;

        var fileComparison = string.Compare(
            x.Value.File.FullPath,
            y.Value.File.FullPath,
            StringComparison.OrdinalIgnoreCase);
        if (fileComparison != 0)
            return fileComparison;

        var xStart = x.Value.Span?.Start ?? int.MaxValue;
        var yStart = y.Value.Span?.Start ?? int.MaxValue;
        var startComparison = xStart.CompareTo(yStart);
        if (startComparison != 0)
            return startComparison;

        var xLength = x.Value.Span?.Length ?? int.MaxValue;
        var yLength = y.Value.Span?.Length ?? int.MaxValue;
        return xLength.CompareTo(yLength);
    }
}
