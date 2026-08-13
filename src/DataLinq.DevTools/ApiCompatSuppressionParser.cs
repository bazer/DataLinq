using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public sealed record ApiCompatSuppressionDiagnostic
{
    private const string FingerprintFormat = "DataLinq ApiCompat suppression diagnostic v1";

    internal ApiCompatSuppressionDiagnostic(
        string diagnosticId,
        string target,
        string left,
        string right,
        bool? isBaselineSuppression)
    {
        DiagnosticId = diagnosticId;
        Target = target;
        Left = left;
        Right = right;
        IsBaselineSuppression = isBaselineSuppression;
        Fingerprint = CreateFingerprint(
            diagnosticId,
            target,
            left,
            right,
            isBaselineSuppression);
    }

    public string DiagnosticId { get; }

    public string Target { get; }

    public string Left { get; }

    public string Right { get; }

    public bool? IsBaselineSuppression { get; }

    public string Fingerprint { get; }

    private static string CreateFingerprint(
        string diagnosticId,
        string target,
        string left,
        string right,
        bool? isBaselineSuppression)
    {
        var builder = new StringBuilder();
        AppendFingerprintValue(builder, FingerprintFormat);
        AppendFingerprintValue(builder, diagnosticId);
        AppendFingerprintValue(builder, target);
        AppendFingerprintValue(builder, left);
        AppendFingerprintValue(builder, right);
        AppendFingerprintValue(
            builder,
            isBaselineSuppression switch
            {
                true => "true",
                false => "false",
                null => "null"
            });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendFingerprintValue(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
}

public static class ApiCompatSuppressionParser
{
    private const long MaximumDocumentCharacters = 64L * 1024 * 1024;
    private const string RootElementName = "Suppressions";
    private const string SuppressionElementName = "Suppression";
    private static readonly HashSet<string> KnownFieldNames =
    [
        "DiagnosticId",
        "Target",
        "Left",
        "Right",
        "IsBaselineSuppression"
    ];

    public static IReadOnlyList<ApiCompatSuppressionDiagnostic> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var canonicalPath = Path.GetFullPath(path);
        using var stream = new FileStream(
            canonicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        return Parse(stream, canonicalPath);
    }

    public static IReadOnlyList<ApiCompatSuppressionDiagnostic> Parse(
        Stream stream,
        string sourceName = "<stream>")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!stream.CanRead)
            throw new ArgumentException("ApiCompat suppression stream must be readable.", nameof(stream));

        try
        {
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    CloseInput = false,
                    DtdProcessing = DtdProcessing.Prohibit,
                    MaxCharactersInDocument = MaximumDocumentCharacters,
                    XmlResolver = null
                });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            return ParseDocument(document);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            throw new InvalidDataException(
                $"ApiCompat suppression XML '{sourceName}' is invalid: {exception.Message} " +
                $"Expected an exact <{RootElementName}> root containing zero or more <{SuppressionElementName}> elements.",
                exception);
        }
    }

    private static IReadOnlyList<ApiCompatSuppressionDiagnostic> ParseDocument(XDocument document)
    {
        var root = document.Root
            ?? throw new InvalidDataException($"Missing required <{RootElementName}> root element.");
        if (root.Name != XName.Get(RootElementName))
        {
            throw new InvalidDataException(
                $"Root element must be exact unqualified <{RootElementName}>, found <{root.Name}>" +
                $"{Location(root)}.");
        }

        ValidateDocumentNodes(document, root);
        var unknownRootAttribute = root.Attributes().FirstOrDefault(static attribute => !attribute.IsNamespaceDeclaration);
        if (unknownRootAttribute is not null)
        {
            throw new InvalidDataException(
                $"<{RootElementName}> has unsupported attribute '{unknownRootAttribute.Name}'" +
                $"{Location(unknownRootAttribute)}; only namespace declarations emitted by ApiCompat are allowed.");
        }

        var diagnostics = new List<ApiCompatSuppressionDiagnostic>();
        var firstDiagnosticIndexByIdentity = new Dictionary<DiagnosticIdentity, int>();
        foreach (var node in root.Nodes())
        {
            if (node is XComment || node is XText text && string.IsNullOrWhiteSpace(text.Value))
                continue;
            if (node is not XElement suppression || suppression.Name != XName.Get(SuppressionElementName))
            {
                throw new InvalidDataException(
                    $"<{RootElementName}> may contain only <{SuppressionElementName}> elements; found " +
                    $"{DescribeNode(node)}{Location(node)}.");
            }

            var diagnosticIndex = diagnostics.Count + 1;
            var diagnostic = ParseSuppression(suppression, diagnosticIndex);
            var identity = new DiagnosticIdentity(
                diagnostic.DiagnosticId,
                diagnostic.Target,
                diagnostic.Left,
                diagnostic.Right,
                diagnostic.IsBaselineSuppression == true);
            if (firstDiagnosticIndexByIdentity.TryGetValue(identity, out var firstIndex))
            {
                throw new InvalidDataException(
                    $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} duplicates diagnostic " +
                    $"{firstIndex.ToString(CultureInfo.InvariantCulture)} for '{diagnostic.DiagnosticId}' and " +
                    $"target '{diagnostic.Target}' with the same left/right and baseline identity; a diagnostic may appear only once.");
            }

            firstDiagnosticIndexByIdentity.Add(identity, diagnosticIndex);
            diagnostics.Add(diagnostic);
        }

        var sortedDiagnostics = diagnostics
            .OrderBy(static diagnostic => diagnostic.DiagnosticId, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Target, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Left, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Right, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.IsBaselineSuppression switch
            {
                null => 0,
                false => 1,
                true => 2
            })
            .ThenBy(static diagnostic => diagnostic.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        return Array.AsReadOnly(sortedDiagnostics);
    }

    private static ApiCompatSuppressionDiagnostic ParseSuppression(XElement suppression, int diagnosticIndex)
    {
        if (suppression.HasAttributes)
        {
            var attribute = suppression.FirstAttribute!;
            throw new InvalidDataException(
                $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} has unsupported attribute " +
                $"'{attribute.Name}'{Location(attribute)}.");
        }

        var fields = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var node in suppression.Nodes())
        {
            if (node is XComment || node is XText text && string.IsNullOrWhiteSpace(text.Value))
                continue;
            if (node is not XElement field)
            {
                throw new InvalidDataException(
                    $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} contains unsupported " +
                    $"{DescribeNode(node)}{Location(node)}.");
            }

            if (field.Name.Namespace != XNamespace.None || !KnownFieldNames.Contains(field.Name.LocalName))
            {
                throw new InvalidDataException(
                    $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} contains unknown field " +
                    $"<{field.Name}>{Location(field)}.");
            }

            if (!fields.TryAdd(field.Name.LocalName, field))
            {
                throw new InvalidDataException(
                    $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} contains duplicate field " +
                    $"<{field.Name.LocalName}>{Location(field)}.");
            }
        }

        var diagnosticId = RequiredField(fields, "DiagnosticId", diagnosticIndex);
        var target = RequiredField(fields, "Target", diagnosticIndex);
        var left = RequiredField(fields, "Left", diagnosticIndex);
        var right = RequiredField(fields, "Right", diagnosticIndex);
        var isBaselineSuppression = OptionalBooleanField(fields, "IsBaselineSuppression", diagnosticIndex);

        return new ApiCompatSuppressionDiagnostic(
            diagnosticId,
            target,
            left,
            right,
            isBaselineSuppression);
    }

    private static string RequiredField(
        IReadOnlyDictionary<string, XElement> fields,
        string fieldName,
        int diagnosticIndex)
    {
        if (!fields.TryGetValue(fieldName, out var field))
        {
            throw new InvalidDataException(
                $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} is missing required " +
                $"<{fieldName}> field.");
        }

        var value = SimpleFieldValue(field, diagnosticIndex).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} field <{fieldName}> " +
                $"must not be blank{Location(field)}.");
        }

        return value;
    }

    private static bool? OptionalBooleanField(
        IReadOnlyDictionary<string, XElement> fields,
        string fieldName,
        int diagnosticIndex)
    {
        if (!fields.TryGetValue(fieldName, out var field))
            return null;

        var value = SimpleFieldValue(field, diagnosticIndex).Trim();
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidDataException(
                $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} field <{fieldName}> " +
                $"must be exact lowercase 'true' or 'false', found '{value}'{Location(field)}.")
        };
    }

    private static string SimpleFieldValue(XElement field, int diagnosticIndex)
    {
        if (field.HasAttributes || field.Nodes().Any(static node => node is not XText))
        {
            throw new InvalidDataException(
                $"Suppression {diagnosticIndex.ToString(CultureInfo.InvariantCulture)} field <{field.Name.LocalName}> " +
                $"must contain text only and no attributes{Location(field)}.");
        }

        return field.Value;
    }

    private static void ValidateDocumentNodes(XDocument document, XElement root)
    {
        foreach (var node in document.Nodes())
        {
            if (ReferenceEquals(node, root) || node is XComment ||
                node is XText text && string.IsNullOrWhiteSpace(text.Value))
            {
                continue;
            }

            throw new InvalidDataException(
                $"Document contains unsupported {DescribeNode(node)} outside <{RootElementName}>" +
                $"{Location(node)}.");
        }
    }

    private static string DescribeNode(XNode node) =>
        node switch
        {
            XElement element => $"element <{element.Name}>",
            XProcessingInstruction instruction => $"processing instruction '{instruction.Target}'",
            XText => "non-whitespace text",
            _ => $"XML node '{node.NodeType}'"
        };

    private static string Location(XObject node)
    {
        if (node is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
        {
            return $" at line {lineInfo.LineNumber.ToString(CultureInfo.InvariantCulture)}, " +
                   $"position {lineInfo.LinePosition.ToString(CultureInfo.InvariantCulture)}";
        }

        return string.Empty;
    }

    private readonly record struct DiagnosticIdentity(
        string DiagnosticId,
        string Target,
        string Left,
        string Right,
        bool IsBaselineSuppression);
}
