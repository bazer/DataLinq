using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace DataLinq.DevTools;

internal sealed record PublicApiSnapshot(
    string AssetName,
    string AssemblyIdentity,
    Guid ModuleVersionId,
    string FileSha256,
    IReadOnlyList<string> AssemblyAttributes,
    IReadOnlyList<string> ModuleAttributes,
    IReadOnlyList<string> ApiLines)
{
    // CanonicalText is exact artifact evidence and intentionally includes the MVID and file hash.
    // SemanticApiText/SemanticApiSha256 exclude that build identity so independently rebuilt
    // assemblies with the same public metadata surface compare equal.
    public string SemanticApiText => string.Join('\n', ApiLines) + "\n";

    public string SemanticApiSha256 => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(SemanticApiText)))
        .ToLowerInvariant();

    public IReadOnlyList<string> CanonicalLines =>
    [
        $"asset {PublicApiSnapshotter.Quote(AssetName)}",
        $"assembly {AssemblyIdentity}",
        $"mvid {ModuleVersionId:D}",
        $"sha256 {FileSha256}",
        .. AssemblyAttributes.Select(static attribute => $"assembly-attribute {attribute}"),
        .. ModuleAttributes.Select(static attribute => $"module-attribute {attribute}"),
        .. ApiLines
    ];

    public string CanonicalText => string.Join('\n', CanonicalLines) + "\n";
}

internal static class PublicApiSnapshotter
{
    private const int MaximumInitialBufferBytes = 4 * 1024 * 1024;
    internal const long MaximumPackageBytes = PackageInspectionPolicy.MaximumPackageArchiveBytes;
    internal const int MaximumAssemblyBytes = PackageInspectionPolicy.MaximumPrimaryManagedAssetBytes;

    public static PublicApiSnapshot SnapshotAssembly(Stream assemblyStream, string assetName)
    {
        ArgumentNullException.ThrowIfNull(assemblyStream);
        if (!assemblyStream.CanRead)
            throw new ArgumentException("The assembly stream must be readable.", nameof(assemblyStream));
        if (string.IsNullOrWhiteSpace(assetName))
            throw new ArgumentException("The asset name must not be blank.", nameof(assetName));

        var image = ReadToEndBounded(
            assemblyStream,
            MaximumAssemblyBytes,
            $"Managed assembly asset {Quote(assetName)}");
        if (image.Length == 0)
            throw new InvalidDataException($"Managed assembly asset {Quote(assetName)} is empty.");

        var sha256 = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();

        try
        {
            using var imageStream = new MemoryStream(image, writable: false);
            using var peReader = new PEReader(imageStream, PEStreamOptions.PrefetchEntireImage);
            if (peReader.PEHeaders.CorHeader is null || !peReader.HasMetadata)
                throw new InvalidDataException($"Asset {Quote(assetName)} is not a managed PE image with metadata.");

            var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);
            if (!reader.IsAssembly)
                throw new InvalidDataException($"Asset {Quote(assetName)} is a managed module, not an assembly.");

            var module = reader.GetModuleDefinition();
            if (module.Mvid.IsNil)
                throw new InvalidDataException($"Managed assembly asset {Quote(assetName)} has no module version id.");

            var formatter = new MetadataFormatter(reader);
            var assemblyDefinition = reader.GetAssemblyDefinition();
            var assemblyAttributes = formatter.FormatCustomAttributes(
                    assemblyDefinition.GetCustomAttributes(),
                    GenericContext.Empty)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            var moduleAttributes = formatter.FormatCustomAttributes(
                    module.GetCustomAttributes(),
                    GenericContext.Empty)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            var apiLines = formatter.CreateApiLines();

            return new PublicApiSnapshot(
                assetName,
                FormatAssemblyIdentity(reader, assemblyDefinition),
                reader.GetGuid(module.Mvid),
                sha256,
                assemblyAttributes,
                moduleAttributes,
                apiLines);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is BadImageFormatException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or IndexOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Asset {Quote(assetName)} does not contain a valid managed assembly image.",
                exception);
        }
    }

    public static PublicApiSnapshot SnapshotPackageAsset(
        Stream packageStream,
        string assetPath)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        if (!packageStream.CanRead)
            throw new ArgumentException("The package stream must be readable.", nameof(packageStream));

        var canonicalPath = NormalizePackageAssetPath(assetPath);
        MemoryStream? packageCopy = null;

        try
        {
            Stream archiveStream;
            if (packageStream.CanSeek && packageStream.Position == 0)
            {
                var packageLength = GetRemainingLength(packageStream, "Package stream");
                if (packageLength == 0)
                    throw new InvalidDataException("The package stream is empty.");
                if (packageLength > MaximumPackageBytes)
                {
                    throw new InvalidDataException(
                        $"Package stream exceeds the {MaximumPackageBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
                }

                archiveStream = packageStream;
            }
            else
            {
                packageCopy = CopyToMemoryBounded(
                    packageStream,
                    MaximumPackageBytes,
                    "Package stream");
                if (packageCopy.Length == 0)
                    throw new InvalidDataException("The package stream is empty.");
                packageCopy.Position = 0;
                archiveStream = packageCopy;
            }

            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            var matches = archive.Entries
                .Where(entry => NormalizeArchiveEntryPath(entry.FullName)
                    .Equals(canonicalPath, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Expected exactly one package asset {Quote(canonicalPath)}, found {matches.Length}.");
            }

            var entry = matches[0];
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException($"Package asset {Quote(canonicalPath)} is a directory entry.");
            if (entry.Length <= 0 || entry.Length > MaximumAssemblyBytes)
            {
                throw new InvalidDataException(
                    $"Package asset {Quote(canonicalPath)} has uncompressed length {entry.Length.ToString(CultureInfo.InvariantCulture)} bytes; expected 1 to {MaximumAssemblyBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
            }

            using var entryStream = entry.Open();
            return SnapshotAssembly(entryStream, canonicalPath);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or NotSupportedException
                                           or ArgumentException)
        {
            throw new InvalidDataException("The package stream is not a valid ZIP/NuGet archive.", exception);
        }
        finally
        {
            packageCopy?.Dispose();
        }
    }

    internal static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                        builder.Append($"\\u{(int)character:x4}");
                    else
                        builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    internal static string FormatGeneralArrayType(string elementType, ArrayShape shape)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementType);
        if (shape.Rank <= 0 || shape.Sizes.Length > shape.Rank || shape.LowerBounds.Length > shape.Rank)
            throw new BadImageFormatException("An ARRAY signature contains an invalid shape.");
        if (shape.Sizes.Any(static size => size < 0))
            throw new BadImageFormatException("An ARRAY signature contains a negative dimension size.");

        var rank = shape.Rank;
        var dimensions = string.Join(
            ",",
            Enumerable.Range(0, rank).Select(index =>
            {
                var lower = index < shape.LowerBounds.Length
                    ? shape.LowerBounds[index].ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                var size = index < shape.Sizes.Length
                    ? shape.Sizes[index].ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                if (lower.Length == 0 && size.Length == 0)
                    return rank == 1 ? "*" : string.Empty;
                return $"lower={lower};size={size}";
            }));
        return $"{elementType}[{dimensions}]";
    }

    internal static string GetGenericParameterName(
        IReadOnlyList<string> names,
        int index,
        string parameterKind)
    {
        if (index < 0 || index >= names.Count)
        {
            throw new BadImageFormatException(
                $"A signature references {parameterKind} generic parameter index {index}, but only {names.Count} {parameterKind} generic parameters are declared.");
        }

        return names[index];
    }

    internal static void ValidateGenericParameterLayout(IReadOnlyList<int> indexes, string owner)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var seen = new bool[indexes.Count];
        foreach (var index in indexes)
        {
            if (index < 0 || index >= seen.Length || seen[index])
            {
                throw new BadImageFormatException(
                    $"The generic parameter rows for {owner} do not contain each zero-based index exactly once.");
            }

            seen[index] = true;
        }
    }

    internal static void ValidateGenericParameterCount(
        int signatureCount,
        int metadataRowCount,
        string owner)
    {
        if (signatureCount < 0 || metadataRowCount < 0 || signatureCount != metadataRowCount)
        {
            throw new BadImageFormatException(
                $"{owner} declares {metadataRowCount} generic parameter rows but its signature declares {signatureCount}.");
        }
    }

    private static byte[] ReadToEndBounded(Stream stream, long maximumBytes, string description)
    {
        using var copy = CopyToMemoryBounded(stream, maximumBytes, description);
        return copy.ToArray();
    }

    private static MemoryStream CopyToMemoryBounded(Stream stream, long maximumBytes, string description)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var initialCapacity = 0;
        if (stream.CanSeek)
        {
            var remainingLength = GetRemainingLength(stream, description);
            if (remainingLength > maximumBytes)
            {
                throw new InvalidDataException(
                    $"{description} exceeds the {maximumBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
            }

            initialCapacity = checked((int)Math.Min(remainingLength, MaximumInitialBufferBytes));
        }

        var copy = new MemoryStream(initialCapacity);
        try
        {
            var buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                    break;
                if (bytesRead < 0 || bytesRead > buffer.Length)
                    throw new InvalidDataException($"{description} returned an invalid read count.");
                if (bytesRead > maximumBytes - totalBytes)
                {
                    throw new InvalidDataException(
                        $"{description} exceeds the {maximumBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
                }

                copy.Write(buffer, 0, bytesRead);
                totalBytes += bytesRead;
            }

            copy.Position = 0;
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private static long GetRemainingLength(Stream stream, string description)
    {
        var length = stream.Length;
        var position = stream.Position;
        if (length < 0 || position < 0 || position > length)
            throw new InvalidDataException($"{description} position is beyond its declared length.");
        return length - position;
    }

    private static string NormalizePackageAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            throw new ArgumentException("The package asset path must not be blank.", nameof(assetPath));
        if (Path.IsPathFullyQualified(assetPath) || assetPath[0] is '/' or '\\')
            throw new ArgumentException("The package asset path must be relative.", nameof(assetPath));

        var normalized = assetPath.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException(
                "The package asset path must be canonical and cannot contain empty, '.' or '..' segments.",
                nameof(assetPath));
        }

        return normalized;
    }

    private static string NormalizeArchiveEntryPath(string path) => path.Replace('\\', '/');

    private static string FormatAssemblyIdentity(
        MetadataReader reader,
        AssemblyDefinition assembly)
    {
        var name = reader.GetString(assembly.Name);
        var culture = assembly.Culture.IsNil ? "neutral" : reader.GetString(assembly.Culture);
        if (string.IsNullOrEmpty(culture))
            culture = "neutral";

        var publicKey = assembly.PublicKey.IsNil
            ? []
            : reader.GetBlobBytes(assembly.PublicKey);
        var publicKeyToken = publicKey.Length == 0 ? "null" : ComputePublicKeyToken(publicKey);
        return $"{name}, Version={assembly.Version}, Culture={culture}, PublicKeyToken={publicKeyToken}";
    }

    private static string ComputePublicKeyToken(byte[] publicKey)
    {
        var hash = SHA1.HashData(publicKey);
        Span<byte> token = stackalloc byte[8];
        for (var index = 0; index < token.Length; index++)
            token[index] = hash[hash.Length - 1 - index];
        return Convert.ToHexString(token).ToLowerInvariant();
    }

    private readonly record struct GenericContext(
        ImmutableArray<string> TypeParameterNames,
        ImmutableArray<string> MethodParameterNames)
    {
        public static GenericContext Empty { get; } = new([], []);
    }

    private readonly record struct ApiType(
        string Text,
        bool IsByReference = false,
        string? NamedTypeFullName = null,
        bool HasRequiredExternalInitModifier = false)
    {
        public string WithReferencePrefix(string prefix) => IsByReference ? $"{prefix} {Text}" : Text;
    }

    private sealed class MetadataFormatter : ISignatureTypeProvider<ApiType, GenericContext>
    {
        private readonly MetadataReader reader;
        private readonly Dictionary<TypeDefinitionHandle, bool> visibility = [];
        private readonly HashSet<TypeDefinitionHandle> visibilityInProgress = [];
        private readonly Dictionary<ExportedTypeHandle, bool> exportedTypeVisibility = [];
        private readonly HashSet<ExportedTypeHandle> exportedTypeVisibilityInProgress = [];

        public MetadataFormatter(MetadataReader reader)
        {
            this.reader = reader;
        }

        public IReadOnlyList<string> CreateApiLines()
        {
            var lines = new List<string>();
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if (reader.GetString(type.Name) == "<Module>" || !IsExternallyVisible(typeHandle))
                    continue;

                AddType(lines, typeHandle, type);
            }

            foreach (var exportedTypeHandle in reader.ExportedTypes)
            {
                var exportedType = reader.GetExportedType(exportedTypeHandle);
                if (!IsTypeForwarder(exportedTypeHandle) || !IsExternallyVisible(exportedTypeHandle))
                    continue;

                var attributes = FormatAttributeSuffix(exportedType.GetCustomAttributes(), GenericContext.Empty);
                lines.Add(
                    $"type-forwarder {GetExportedTypeAccessibility(exportedType)} {GetExportedTypeFullName(exportedTypeHandle)} -> {GetExportedTypeResolutionScope(exportedTypeHandle)}{attributes}");
            }

            return lines.OrderBy(static line => line, StringComparer.Ordinal).ToArray();
        }

        public IEnumerable<string> FormatCustomAttributes(
            CustomAttributeHandleCollection handles,
            GenericContext context)
        {
            var attributes = new List<string>();
            foreach (var handle in handles)
            {
                var attribute = reader.GetCustomAttribute(handle);
                var constructor = FormatAttributeConstructor(attribute.Constructor, context);
                var blob = Convert.ToHexString(reader.GetBlobBytes(attribute.Value)).ToLowerInvariant();
                attributes.Add($"{constructor}=0x{blob}");
            }

            return attributes.OrderBy(static attribute => attribute, StringComparer.Ordinal);
        }

        public ApiType GetArrayType(ApiType elementType, ArrayShape shape)
            => new(FormatGeneralArrayType(elementType.Text, shape));

        public ApiType GetByReferenceType(ApiType elementType) => elementType with { IsByReference = true };

        public ApiType GetFunctionPointerType(MethodSignature<ApiType> signature)
        {
            var parameters = signature.ParameterTypes.Select(static parameter => parameter.WithReferencePrefix("ref"));
            return new ApiType(
                $"fnptr[{signature.Header.CallingConvention}]({string.Join(",", parameters)})->{signature.ReturnType.WithReferencePrefix("ref")}");
        }

        public ApiType GetGenericInstantiation(ApiType genericType, ImmutableArray<ApiType> typeArguments) =>
            new($"{genericType.Text}<{string.Join(",", typeArguments.Select(static argument => argument.Text))}>");

        public ApiType GetGenericMethodParameter(GenericContext genericContext, int index) =>
            new(PublicApiSnapshotter.GetGenericParameterName(
                genericContext.MethodParameterNames,
                index,
                "method"));

        public ApiType GetGenericTypeParameter(GenericContext genericContext, int index) =>
            new(PublicApiSnapshotter.GetGenericParameterName(
                genericContext.TypeParameterNames,
                index,
                "type"));

        public ApiType GetModifiedType(ApiType modifier, ApiType unmodifiedType, bool isRequired) =>
            unmodifiedType with
            {
                Text = $"{(isRequired ? "modreq" : "modopt")}({modifier.Text}) {unmodifiedType.Text}",
                HasRequiredExternalInitModifier = unmodifiedType.HasRequiredExternalInitModifier ||
                                                  isRequired &&
                                                  modifier.NamedTypeFullName ==
                                                  "System.Runtime.CompilerServices.IsExternalInit"
            };

        public ApiType GetPinnedType(ApiType elementType) => elementType with { Text = $"pinned {elementType.Text}" };

        public ApiType GetPointerType(ApiType elementType) => new($"{elementType.Text}*");

        public ApiType GetPrimitiveType(PrimitiveTypeCode typeCode) => new(typeCode switch
        {
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "int8",
            PrimitiveTypeCode.Byte => "uint8",
            PrimitiveTypeCode.Int16 => "int16",
            PrimitiveTypeCode.UInt16 => "uint16",
            PrimitiveTypeCode.Int32 => "int32",
            PrimitiveTypeCode.UInt32 => "uint32",
            PrimitiveTypeCode.Int64 => "int64",
            PrimitiveTypeCode.UInt64 => "uint64",
            PrimitiveTypeCode.Single => "float32",
            PrimitiveTypeCode.Double => "float64",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.TypedReference => "typedref",
            PrimitiveTypeCode.IntPtr => "native int",
            PrimitiveTypeCode.UIntPtr => "native uint",
            PrimitiveTypeCode.Object => "object",
            _ => $"primitive(0x{(byte)typeCode:x2})"
        });

        public ApiType GetSZArrayType(ApiType elementType) => new($"{elementType.Text}[]");

        public ApiType GetTypeFromDefinition(
            MetadataReader metadataReader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            var fullName = GetTypeDefinitionFullName(handle);
            return new ApiType($"{FormatRawTypeKind(rawTypeKind)}{fullName}", NamedTypeFullName: fullName);
        }

        public ApiType GetTypeFromReference(
            MetadataReader metadataReader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var fullName = GetTypeReferenceFullName(handle);
            return new ApiType(
                $"{FormatRawTypeKind(rawTypeKind)}[{GetTypeReferenceResolutionScope(handle)}]{fullName}",
                NamedTypeFullName: fullName);
        }

        public ApiType GetTypeFromSpecification(
            MetadataReader metadataReader,
            GenericContext genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        private static string FormatRawTypeKind(byte rawTypeKind) => rawTypeKind switch
        {
            0 => string.Empty,
            (byte)SignatureTypeKind.Class => "class ",
            (byte)SignatureTypeKind.ValueType => "valuetype ",
            _ => $"type-kind(0x{rawTypeKind:x2}) "
        };

        private void AddType(
            ICollection<string> lines,
            TypeDefinitionHandle typeHandle,
            TypeDefinition type)
        {
            var typeContext = CreateTypeContext(type);
            var typeName = GetTypeDefinitionFullName(typeHandle);
            var kind = GetTypeKind(type, typeContext);
            var modifiers = FormatTypeModifiers(type.Attributes);
            var relations = FormatTypeRelations(type, typeContext, kind);
            var genericParameters = FormatGenericParameters(type.GetGenericParameters(), typeContext);
            var attributes = FormatAttributeSuffix(type.GetCustomAttributes(), typeContext);
            lines.Add(
                $"type {GetTypeAccessibility(type.Attributes)} {modifiers}{kind} {typeName}{genericParameters}{relations}{attributes}");

            var accessorMethods = new HashSet<MethodDefinitionHandle>();
            foreach (var propertyHandle in type.GetProperties())
            {
                var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
                AddAccessorHandles(accessorMethods, accessors.Getter, accessors.Setter, accessors.Others);
            }

            foreach (var eventHandle in type.GetEvents())
            {
                var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
                AddAccessorHandles(accessorMethods, accessors.Adder, accessors.Remover, accessors.Raiser, accessors.Others);
            }

            foreach (var fieldHandle in type.GetFields())
                AddField(lines, typeName, fieldHandle, typeContext, kind);
            foreach (var methodHandle in type.GetMethods())
            {
                if (!accessorMethods.Contains(methodHandle))
                    AddMethod(lines, typeName, methodHandle, typeContext);
            }
            foreach (var propertyHandle in type.GetProperties())
                AddProperty(lines, typeName, propertyHandle, typeContext);
            foreach (var eventHandle in type.GetEvents())
                AddEvent(lines, typeName, eventHandle, typeContext);
        }

        private void AddField(
            ICollection<string> lines,
            string declaringType,
            FieldDefinitionHandle handle,
            GenericContext context,
            string typeKind)
        {
            var field = reader.GetFieldDefinition(handle);
            if (!IsExternallyVisible(field.Attributes))
                return;

            var name = reader.GetString(field.Name);
            if (typeKind == "enum" && name == "value__")
                return;

            var fieldType = field.DecodeSignature(this, context);
            var modifiers = new List<string>();
            if ((field.Attributes & FieldAttributes.Literal) != 0)
                modifiers.Add("const");
            else
            {
                if ((field.Attributes & FieldAttributes.Static) != 0)
                    modifiers.Add("static");
                if ((field.Attributes & FieldAttributes.InitOnly) != 0)
                    modifiers.Add("readonly");
            }
            if ((field.Attributes & FieldAttributes.SpecialName) != 0)
                modifiers.Add("specialname");

            var modifierText = modifiers.Count == 0 ? string.Empty : string.Join(' ', modifiers) + " ";
            var defaultValue = field.GetDefaultValue();
            var value = defaultValue.IsNil ? string.Empty : $" value={FormatConstant(defaultValue)}";
            var attributes = FormatAttributeSuffix(field.GetCustomAttributes(), context);
            lines.Add(
                $"member {declaringType} :: field {GetFieldAccessibility(field.Attributes)} {modifierText}{fieldType.Text} {EscapeName(name)}{value}{attributes}");
        }

        private void AddMethod(
            ICollection<string> lines,
            string declaringType,
            MethodDefinitionHandle handle,
            GenericContext typeContext)
        {
            var method = reader.GetMethodDefinition(handle);
            if (!IsExternallyVisible(method.Attributes))
                return;

            var methodParameterNames = GetGenericParameterNames(
                method.GetGenericParameters(),
                $"method {GetTypeDefinitionFullName(method.GetDeclaringType())}::{reader.GetString(method.Name)}");
            var context = typeContext with { MethodParameterNames = methodParameterNames };
            var signature = method.DecodeSignature(this, context);
            PublicApiSnapshotter.ValidateGenericParameterCount(
                signature.GenericParameterCount,
                methodParameterNames.Length,
                $"Method {GetTypeDefinitionFullName(method.GetDeclaringType())}::{reader.GetString(method.Name)}");
            var name = reader.GetString(method.Name);
            var kind = name is ".ctor" or ".cctor" ? "constructor" : "method";
            var genericNames = methodParameterNames.Length == 0
                ? string.Empty
                : $"<{string.Join(",", methodParameterNames.Select(EscapeName))}>";
            var parameters = FormatMethodParameters(method, signature, context);
            var genericParameters = FormatGenericParameters(method.GetGenericParameters(), context);
            var modifiers = FormatMethodModifiers(method.Attributes);
            var attributes = FormatAttributeSuffix(method.GetCustomAttributes(), context);
            var returnAttributes = FormatReturnAttributeSuffix(method, context);
            var returnType = signature.ReturnType.WithReferencePrefix(
                HasAttribute(method.GetParameters(), 0, "System.Runtime.CompilerServices.IsReadOnlyAttribute")
                    ? "ref readonly"
                    : "ref");

            if (kind == "constructor")
            {
                lines.Add(
                    $"member {declaringType} :: constructor {GetMethodAccessibility(method.Attributes)} {modifiers}{name}({parameters}){attributes}");
                return;
            }

            lines.Add(
                $"member {declaringType} :: method {GetMethodAccessibility(method.Attributes)} {modifiers}{returnType} {EscapeName(name)}{genericNames}({parameters}){genericParameters}{attributes}{returnAttributes}");
        }

        private void AddProperty(
            ICollection<string> lines,
            string declaringType,
            PropertyDefinitionHandle handle,
            GenericContext context)
        {
            var property = reader.GetPropertyDefinition(handle);
            var accessors = property.GetAccessors();
            var visibleGetter = IsExternallyVisible(accessors.Getter);
            var visibleSetter = IsExternallyVisible(accessors.Setter);
            var visibleOthers = accessors.Others.Where(IsExternallyVisible).ToArray();
            if (!visibleGetter && !visibleSetter && visibleOthers.Length == 0)
                return;

            var signature = property.DecodeSignature(this, context);
            var representative = visibleGetter ? accessors.Getter : accessors.Setter;
            if (representative.IsNil)
                representative = visibleOthers[0];
            var representativeMethod = reader.GetMethodDefinition(representative);
            var propertyParameters = FormatPropertyParameters(
                signature,
                visibleGetter ? accessors.Getter : accessors.Setter,
                context);
            var access = GetMostVisibleAccessibility(
                EnumerateAccessorHandles(accessors.Getter, accessors.Setter, accessors.Others));
            var modifiers = FormatMethodModifiers(representativeMethod.Attributes, includeSpecialName: false);
            var propertyType = signature.ReturnType.WithReferencePrefix("ref");
            var name = reader.GetString(property.Name);
            var accessorText = FormatPropertyAccessors(accessors, context);
            var attributes = FormatAttributeSuffix(property.GetCustomAttributes(), context);
            var returnAttributes = visibleGetter
                ? FormatReturnAttributeSuffix(reader.GetMethodDefinition(accessors.Getter), context)
                : string.Empty;
            var parameterList = propertyParameters.Length == 0 ? string.Empty : $"[{propertyParameters}]";
            lines.Add(
                $"member {declaringType} :: property {access} {modifiers}{propertyType} {EscapeName(name)}{parameterList} {{{accessorText}}}{attributes}{returnAttributes}");
        }

        private void AddEvent(
            ICollection<string> lines,
            string declaringType,
            EventDefinitionHandle handle,
            GenericContext context)
        {
            var @event = reader.GetEventDefinition(handle);
            var accessors = @event.GetAccessors();
            var visibleHandles = EnumerateAccessorHandles(
                    accessors.Adder,
                    accessors.Remover,
                    accessors.Raiser,
                    accessors.Others)
                .Where(IsExternallyVisible)
                .ToArray();
            if (visibleHandles.Length == 0)
                return;

            var representative = reader.GetMethodDefinition(visibleHandles[0]);
            var eventType = DecodeEntityType(@event.Type, context).Text;
            var name = reader.GetString(@event.Name);
            var access = GetMostVisibleAccessibility(visibleHandles);
            var modifiers = FormatMethodModifiers(representative.Attributes, includeSpecialName: false);
            var accessorText = FormatEventAccessors(accessors, context);
            var attributes = FormatAttributeSuffix(@event.GetCustomAttributes(), context);
            lines.Add(
                $"member {declaringType} :: event {access} {modifiers}{eventType} {EscapeName(name)} {{{accessorText}}}{attributes}");
        }

        private string FormatMethodParameters(
            MethodDefinition method,
            MethodSignature<ApiType> signature,
            GenericContext context)
        {
            var rows = method.GetParameters()
                .Select(reader.GetParameter)
                .Where(static parameter => parameter.SequenceNumber > 0)
                .ToDictionary(static parameter => (int)parameter.SequenceNumber);
            var parameters = new List<string>();
            for (var index = 0; index < signature.ParameterTypes.Length; index++)
            {
                if (index == signature.RequiredParameterCount &&
                    signature.RequiredParameterCount < signature.ParameterTypes.Length)
                {
                    parameters.Add("varargs");
                }

                var parameter = rows.TryGetValue(index + 1, out var row) ? row : (Parameter?)null;
                parameters.Add(FormatParameter(signature.ParameterTypes[index], parameter, index, context));
            }

            return string.Join(", ", parameters);
        }

        private string FormatPropertyParameters(
            MethodSignature<ApiType> propertySignature,
            MethodDefinitionHandle accessorHandle,
            GenericContext context)
        {
            if (propertySignature.ParameterTypes.Length == 0)
                return string.Empty;

            var rows = accessorHandle.IsNil
                ? new Dictionary<int, Parameter>()
                : reader.GetMethodDefinition(accessorHandle).GetParameters()
                    .Select(reader.GetParameter)
                    .Where(static parameter => parameter.SequenceNumber > 0)
                    .ToDictionary(static parameter => (int)parameter.SequenceNumber);
            var parameters = new List<string>();
            for (var index = 0; index < propertySignature.ParameterTypes.Length; index++)
            {
                var parameter = rows.TryGetValue(index + 1, out var row) ? row : (Parameter?)null;
                parameters.Add(FormatParameter(propertySignature.ParameterTypes[index], parameter, index, context));
            }

            return string.Join(", ", parameters);
        }

        private string FormatParameter(
            ApiType type,
            Parameter? parameter,
            int index,
            GenericContext context)
        {
            var attributes = parameter?.Attributes ?? 0;
            var prefix = string.Empty;
            if (type.IsByReference)
            {
                prefix = (attributes & ParameterAttributes.Out) != 0 &&
                         (attributes & ParameterAttributes.In) == 0
                    ? "out "
                    : (attributes & ParameterAttributes.In) != 0 &&
                      (attributes & ParameterAttributes.Out) == 0
                        ? "in "
                        : "ref ";
            }

            var name = parameter is null || parameter.Value.Name.IsNil
                ? $"arg{index}"
                : reader.GetString(parameter.Value.Name);
            var flags = new List<string>();
            if ((attributes & ParameterAttributes.Optional) != 0)
                flags.Add("optional");
            var defaultValue = parameter?.GetDefaultValue() ?? default;
            if (!defaultValue.IsNil)
                flags.Add($"default={FormatConstant(defaultValue)}");
            var customAttributes = parameter is null
                ? []
                : FormatCustomAttributes(parameter.Value.GetCustomAttributes(), context).ToArray();
            if (customAttributes.Length > 0)
                flags.Add($"attrs=[{string.Join(";", customAttributes)}]");
            var suffix = flags.Count == 0 ? string.Empty : $" {{{string.Join(";", flags)}}}";
            return $"{prefix}{type.Text} {EscapeName(name)}{suffix}";
        }

        private string FormatPropertyAccessors(PropertyAccessors accessors, GenericContext context)
        {
            var values = new List<string>();
            AddAccessor(values, "get", accessors.Getter, context);
            AddAccessor(values, "set", accessors.Setter, context);
            foreach (var other in accessors.Others)
                AddAccessor(values, "other", other, context);
            return string.Join("; ", values);
        }

        private string FormatEventAccessors(EventAccessors accessors, GenericContext context)
        {
            var values = new List<string>();
            AddAccessor(values, "add", accessors.Adder, context);
            AddAccessor(values, "remove", accessors.Remover, context);
            AddAccessor(values, "raise", accessors.Raiser, context);
            foreach (var other in accessors.Others)
                AddAccessor(values, "other", other, context);
            return string.Join("; ", values);
        }

        private void AddAccessor(
            ICollection<string> values,
            string kind,
            MethodDefinitionHandle handle,
            GenericContext context)
        {
            if (handle.IsNil)
                return;
            var method = reader.GetMethodDefinition(handle);
            if (!IsExternallyVisible(method.Attributes))
                return;
            if (kind == "set")
            {
                var signature = method.DecodeSignature(this, context);
                if (signature.ReturnType.HasRequiredExternalInitModifier)
                    kind = "init";
            }
            var attributes = FormatAttributeSuffix(method.GetCustomAttributes(), context);
            var modifiers = FormatMethodModifiers(method.Attributes, includeSpecialName: false).TrimEnd();
            var modifierSuffix = modifiers.Length == 0 ? string.Empty : $" {modifiers}";
            values.Add($"{kind}:{GetMethodAccessibility(method.Attributes)}{modifierSuffix}{attributes}");
        }

        private string FormatTypeRelations(
            TypeDefinition type,
            GenericContext context,
            string kind)
        {
            var parts = new List<string>();
            if (kind == "enum")
            {
                FieldDefinitionHandle underlyingHandle = default;
                foreach (var fieldHandle in type.GetFields())
                {
                    if (reader.GetString(reader.GetFieldDefinition(fieldHandle).Name) == "value__")
                    {
                        underlyingHandle = fieldHandle;
                        break;
                    }
                }

                if (underlyingHandle.IsNil)
                    throw new BadImageFormatException("An enum type has no value__ field.");
                parts.Add(
                    $"underlying={reader.GetFieldDefinition(underlyingHandle).DecodeSignature(this, context).Text}");
            }
            else if (!type.BaseType.IsNil)
            {
                parts.Add($"base={DecodeEntityType(type.BaseType, context).Text}");
            }

            var interfaces = type.GetInterfaceImplementations()
                .Select(handle =>
                {
                    var implementation = reader.GetInterfaceImplementation(handle);
                    var relationAttributes = FormatCustomAttributes(implementation.GetCustomAttributes(), context).ToArray();
                    var attributeSuffix = relationAttributes.Length == 0
                        ? string.Empty
                        : $"{{attrs=[{string.Join(";", relationAttributes)}]}}";
                    return DecodeEntityType(implementation.Interface, context).Text + attributeSuffix;
                })
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            if (interfaces.Length > 0)
                parts.Add($"interfaces=[{string.Join(";", interfaces)}]");

            return parts.Count == 0 ? string.Empty : " " + string.Join(' ', parts);
        }

        private string FormatGenericParameters(
            GenericParameterHandleCollection handles,
            GenericContext context)
        {
            var values = handles
                .Select(handle =>
                {
                    var parameter = reader.GetGenericParameter(handle);
                    var name = reader.GetString(parameter.Name);
                    var constraints = new List<string>();
                    var variance = parameter.Attributes & GenericParameterAttributes.VarianceMask;
                    if (variance == GenericParameterAttributes.Covariant)
                        constraints.Add("covariant");
                    else if (variance == GenericParameterAttributes.Contravariant)
                        constraints.Add("contravariant");
                    if ((parameter.Attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                        constraints.Add("class");
                    if ((parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                        constraints.Add("valuetype");
                    if ((parameter.Attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
                        constraints.Add("new()");

                    constraints.AddRange(parameter.GetConstraints()
                        .Select(constraintHandle =>
                        {
                            var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                            var attributes = FormatCustomAttributes(constraint.GetCustomAttributes(), context).ToArray();
                            var suffix = attributes.Length == 0
                                ? string.Empty
                                : $"{{attrs=[{string.Join(";", attributes)}]}}";
                            return DecodeEntityType(constraint.Type, context).Text + suffix;
                        })
                        .OrderBy(static value => value, StringComparer.Ordinal));
                    var parameterAttributes = FormatCustomAttributes(parameter.GetCustomAttributes(), context).ToArray();
                    var attributesSuffix = parameterAttributes.Length == 0
                        ? string.Empty
                        : $";attrs=[{string.Join(";", parameterAttributes)}]";
                    return (
                        Index: parameter.Index,
                        Text: $"{EscapeName(name)}{{flags=0x{(int)parameter.Attributes:x4};constraints=[{string.Join(";", constraints)}]{attributesSuffix}}}");
                })
                .OrderBy(static value => value.Index)
                .Select(static value => value.Text)
                .ToArray();

            return values.Length == 0 ? string.Empty : $" generic=[{string.Join(";", values)}]";
        }

        private string FormatAttributeSuffix(
            CustomAttributeHandleCollection handles,
            GenericContext context)
        {
            var attributes = FormatCustomAttributes(handles, context).ToArray();
            return attributes.Length == 0 ? string.Empty : $" attrs=[{string.Join(";", attributes)}]";
        }

        private string FormatReturnAttributeSuffix(MethodDefinition method, GenericContext context)
        {
            Parameter? returnParameter = null;
            foreach (var handle in method.GetParameters())
            {
                var parameter = reader.GetParameter(handle);
                if (parameter.SequenceNumber == 0)
                {
                    returnParameter = parameter;
                    break;
                }
            }

            if (returnParameter is null)
                return string.Empty;
            var attributes = FormatCustomAttributes(returnParameter.Value.GetCustomAttributes(), context).ToArray();
            return attributes.Length == 0 ? string.Empty : $" return-attrs=[{string.Join(";", attributes)}]";
        }

        private string FormatAttributeConstructor(EntityHandle handle, GenericContext context)
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var declaringType = method.GetDeclaringType();
                    var constructorContext = CreateTypeContext(reader.GetTypeDefinition(declaringType));
                    var signature = method.DecodeSignature(this, constructorContext);
                    return $"{GetTypeDefinitionFullName(declaringType)}::{reader.GetString(method.Name)}({string.Join(",", signature.ParameterTypes.Select(static type => type.Text))})";
                }
                case HandleKind.MemberReference:
                {
                    var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                    if (member.GetKind() != MemberReferenceKind.Method)
                        throw new BadImageFormatException("A custom attribute constructor is not a method reference.");
                    var signature = member.DecodeMethodSignature(this, context);
                    return $"{FormatMemberReferenceParent(member.Parent, context)}::{reader.GetString(member.Name)}({string.Join(",", signature.ParameterTypes.Select(static type => type.Text))})";
                }
                default:
                    throw new BadImageFormatException(
                        $"Unsupported custom attribute constructor handle kind {handle.Kind}.");
            }
        }

        private string FormatMemberReferenceParent(EntityHandle handle, GenericContext context) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeDefinitionFullName((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0).Text,
            HandleKind.TypeSpecification => DecodeEntityType(handle, context).Text,
            HandleKind.MethodDefinition => GetTypeDefinitionFullName(
                reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType()),
            HandleKind.ModuleReference => $"module:{reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)handle).Name)}",
            _ => throw new BadImageFormatException($"Unsupported member reference parent handle kind {handle.Kind}.")
        };

        private ApiType DecodeEntityType(EntityHandle handle, GenericContext context) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(reader, context, (TypeSpecificationHandle)handle, 0),
            _ => throw new BadImageFormatException($"Unsupported type handle kind {handle.Kind}.")
        };

        private string FormatConstant(ConstantHandle handle)
        {
            var constant = reader.GetConstant(handle);
            var bytes = reader.GetBlobBytes(constant.Value);
            var blob = reader.GetBlobReader(constant.Value);
            return constant.TypeCode switch
            {
                ConstantTypeCode.Boolean => blob.ReadByte() == 0 ? "false" : "true",
                ConstantTypeCode.Char => FormatChar((char)blob.ReadUInt16()),
                ConstantTypeCode.SByte => blob.ReadSByte().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Byte => blob.ReadByte().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int16 => blob.ReadInt16().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int32 => blob.ReadInt32().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int64 => blob.ReadInt64().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.UInt64 => blob.ReadUInt64().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Single => blob.ReadSingle().ToString("R", CultureInfo.InvariantCulture),
                ConstantTypeCode.Double => blob.ReadDouble().ToString("R", CultureInfo.InvariantCulture),
                ConstantTypeCode.String => Quote(Encoding.Unicode.GetString(bytes)),
                ConstantTypeCode.NullReference => "null",
                _ => $"constant({constant.TypeCode},0x{Convert.ToHexString(bytes).ToLowerInvariant()})"
            };
        }

        private bool HasAttribute(
            ParameterHandleCollection parameters,
            int sequenceNumber,
            string attributeTypeName)
        {
            foreach (var parameterHandle in parameters)
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber != sequenceNumber)
                    continue;
                foreach (var attributeHandle in parameter.GetCustomAttributes())
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);
                    if (GetAttributeTypeName(attribute.Constructor) == attributeTypeName)
                        return true;
                }
            }

            return false;
        }

        private string GetAttributeTypeName(EntityHandle constructor) => constructor.Kind switch
        {
            HandleKind.MethodDefinition => GetTypeDefinitionFullName(
                reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
            HandleKind.MemberReference => GetNamedTypeFullName(
                reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                GenericContext.Empty),
            _ => throw new BadImageFormatException(
                $"Unsupported custom attribute constructor handle kind {constructor.Kind}.")
        };

        private string GetNamedTypeFullName(EntityHandle handle, GenericContext context) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeDefinitionFullName((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeReferenceFullName((TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => DecodeEntityType(handle, context).NamedTypeFullName ??
                                            DecodeEntityType(handle, context).Text,
            _ => throw new BadImageFormatException($"Unsupported named type handle kind {handle.Kind}.")
        };

        private GenericContext CreateTypeContext(TypeDefinition type) =>
            new(
                GetGenericParameterNames(
                    type.GetGenericParameters(),
                    $"type {reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}"),
                []);

        private ImmutableArray<string> GetGenericParameterNames(
            GenericParameterHandleCollection handles,
            string owner)
        {
            var parameters = handles.Select(reader.GetGenericParameter).ToArray();
            PublicApiSnapshotter.ValidateGenericParameterLayout(
                parameters.Select(static parameter => (int)parameter.Index).ToArray(),
                owner);
            var names = new string?[parameters.Length];
            foreach (var parameter in parameters)
            {
                var index = parameter.Index;
                names[index] = reader.GetString(parameter.Name);
            }

            return names.Select(static name => name!).ToImmutableArray();
        }

        private string GetTypeKind(TypeDefinition type, GenericContext context)
        {
            if ((type.Attributes & TypeAttributes.Interface) != 0)
                return "interface";
            if (type.BaseType.IsNil)
                return "class";
            return DecodeEntityType(type.BaseType, context).NamedTypeFullName switch
            {
                "System.Enum" => "enum",
                "System.ValueType" => "struct",
                "System.MulticastDelegate" or "System.Delegate" => "delegate",
                _ => "class"
            };
        }

        private bool IsExternallyVisible(TypeDefinitionHandle handle)
        {
            if (visibility.TryGetValue(handle, out var result))
                return result;

            if (!visibilityInProgress.Add(handle))
                throw new BadImageFormatException("Nested type visibility contains a declaring-type cycle.");

            try
            {
                var type = reader.GetTypeDefinition(handle);
                var typeVisibility = type.Attributes & TypeAttributes.VisibilityMask;
                result = typeVisibility switch
                {
                    TypeAttributes.Public => true,
                    TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem =>
                        !type.GetDeclaringType().IsNil && IsExternallyVisible(type.GetDeclaringType()),
                    _ => false
                };
                visibility[handle] = result;
                return result;
            }
            finally
            {
                visibilityInProgress.Remove(handle);
            }
        }

        private bool IsExternallyVisible(MethodDefinitionHandle handle) =>
            !handle.IsNil && IsExternallyVisible(reader.GetMethodDefinition(handle).Attributes);

        private static bool IsExternallyVisible(MethodAttributes attributes) =>
            (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public
                or MethodAttributes.Family
                or MethodAttributes.FamORAssem;

        private static bool IsExternallyVisible(FieldAttributes attributes) =>
            (attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public
                or FieldAttributes.Family
                or FieldAttributes.FamORAssem;

        private string GetTypeDefinitionFullName(TypeDefinitionHandle handle)
        {
            var names = new List<string>();
            var seen = new HashSet<TypeDefinitionHandle>();
            var current = handle;
            var @namespace = string.Empty;
            while (!current.IsNil)
            {
                if (!seen.Add(current))
                    throw new BadImageFormatException("Nested type metadata contains a declaring-type cycle.");
                var type = reader.GetTypeDefinition(current);
                names.Add(EscapeName(reader.GetString(type.Name)));
                current = type.GetDeclaringType();
                if (current.IsNil)
                    @namespace = reader.GetString(type.Namespace);
            }

            names.Reverse();
            var name = string.Join('+', names);
            return string.IsNullOrEmpty(@namespace) ? name : $"{EscapeName(@namespace)}.{name}";
        }

        private string GetTypeReferenceFullName(TypeReferenceHandle handle)
        {
            var names = new List<string>();
            var seen = new HashSet<TypeReferenceHandle>();
            var current = handle;
            var @namespace = string.Empty;
            while (!current.IsNil)
            {
                if (!seen.Add(current))
                    throw new BadImageFormatException("Nested type reference metadata contains a scope cycle.");
                var type = reader.GetTypeReference(current);
                names.Add(EscapeName(reader.GetString(type.Name)));
                if (type.ResolutionScope.Kind != HandleKind.TypeReference)
                {
                    @namespace = reader.GetString(type.Namespace);
                    break;
                }

                current = (TypeReferenceHandle)type.ResolutionScope;
            }

            names.Reverse();
            var name = string.Join('+', names);
            return string.IsNullOrEmpty(@namespace) ? name : $"{EscapeName(@namespace)}.{name}";
        }

        private string GetTypeReferenceResolutionScope(TypeReferenceHandle handle)
        {
            var seen = new HashSet<TypeReferenceHandle>();
            var current = handle;
            while (true)
            {
                if (!seen.Add(current))
                    throw new BadImageFormatException("Nested type reference metadata contains a scope cycle.");

                var scope = reader.GetTypeReference(current).ResolutionScope;
                switch (scope.Kind)
                {
                    case HandleKind.TypeReference:
                        current = (TypeReferenceHandle)scope;
                        continue;
                    case HandleKind.AssemblyReference:
                        return $"assembly-ref:{FormatAssemblyReference((AssemblyReferenceHandle)scope)}";
                    case HandleKind.ModuleReference:
                        return $"module-ref:{Quote(reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)scope).Name))}";
                    case HandleKind.ModuleDefinition:
                        return $"module:{Quote(reader.GetString(reader.GetModuleDefinition().Name))}";
                    default:
                        throw new BadImageFormatException(
                            $"A type reference has unsupported resolution scope {scope.Kind}.");
                }
            }
        }

        private string GetExportedTypeFullName(ExportedTypeHandle handle)
        {
            var names = new List<string>();
            var seen = new HashSet<ExportedTypeHandle>();
            var current = handle;
            var @namespace = string.Empty;
            while (true)
            {
                if (!seen.Add(current))
                    throw new BadImageFormatException("Nested exported type metadata contains an implementation cycle.");

                var type = reader.GetExportedType(current);
                names.Add(EscapeName(reader.GetString(type.Name)));
                if (type.Implementation.Kind != HandleKind.ExportedType)
                {
                    @namespace = reader.GetString(type.Namespace);
                    break;
                }

                current = (ExportedTypeHandle)type.Implementation;
            }

            names.Reverse();
            var name = string.Join('+', names);
            return string.IsNullOrEmpty(@namespace) ? name : $"{EscapeName(@namespace)}.{name}";
        }

        private string GetExportedTypeResolutionScope(ExportedTypeHandle handle)
        {
            var seen = new HashSet<ExportedTypeHandle>();
            var current = handle;
            while (true)
            {
                if (!seen.Add(current))
                    throw new BadImageFormatException("Nested exported type metadata contains an implementation cycle.");

                var implementation = reader.GetExportedType(current).Implementation;
                switch (implementation.Kind)
                {
                    case HandleKind.ExportedType:
                        current = (ExportedTypeHandle)implementation;
                        continue;
                    case HandleKind.AssemblyReference:
                        return $"assembly-ref:{FormatAssemblyReference((AssemblyReferenceHandle)implementation)}";
                    case HandleKind.AssemblyFile:
                    {
                        var file = reader.GetAssemblyFile((AssemblyFileHandle)implementation);
                        return $"assembly-file:{Quote(reader.GetString(file.Name))}";
                    }
                    default:
                        throw new BadImageFormatException(
                            $"A forwarded type has unsupported implementation scope {implementation.Kind}.");
                }
            }
        }

        private bool IsTypeForwarder(ExportedTypeHandle handle)
        {
            var seen = new HashSet<ExportedTypeHandle>();
            var current = handle;
            while (true)
            {
                if (!seen.Add(current))
                    throw new BadImageFormatException("Nested exported type metadata contains an implementation cycle.");

                var type = reader.GetExportedType(current);
                if (type.IsForwarder)
                    return true;
                if (type.Implementation.Kind != HandleKind.ExportedType)
                    return false;
                current = (ExportedTypeHandle)type.Implementation;
            }
        }

        private string FormatAssemblyReference(AssemblyReferenceHandle handle)
        {
            var reference = reader.GetAssemblyReference(handle);
            var name = reader.GetString(reference.Name);
            var culture = reference.Culture.IsNil ? "neutral" : reader.GetString(reference.Culture);
            if (string.IsNullOrEmpty(culture))
                culture = "neutral";

            var keyOrToken = reference.PublicKeyOrToken.IsNil
                ? []
                : reader.GetBlobBytes(reference.PublicKeyOrToken);
            var publicKeyToken = keyOrToken.Length == 0
                ? "null"
                : (reference.Flags & AssemblyFlags.PublicKey) != 0
                    ? ComputePublicKeyToken(keyOrToken)
                    : Convert.ToHexString(keyOrToken).ToLowerInvariant();
            return $"{name}, Version={reference.Version}, Culture={culture}, PublicKeyToken={publicKeyToken}, Flags=0x{(int)reference.Flags:x8}";
        }

        private bool IsExternallyVisible(ExportedTypeHandle handle)
        {
            if (exportedTypeVisibility.TryGetValue(handle, out var result))
                return result;

            if (!exportedTypeVisibilityInProgress.Add(handle))
                throw new BadImageFormatException("Exported type visibility contains an implementation cycle.");

            try
            {
                var type = reader.GetExportedType(handle);
                var typeVisibility = type.Attributes & TypeAttributes.VisibilityMask;
                result = typeVisibility switch
                {
                    TypeAttributes.Public => true,
                    TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem =>
                        type.Implementation.Kind == HandleKind.ExportedType &&
                        IsExternallyVisible((ExportedTypeHandle)type.Implementation),
                    _ => type.IsForwarder && type.Implementation.Kind == HandleKind.AssemblyReference
                };
                exportedTypeVisibility[handle] = result;
                return result;
            }
            finally
            {
                exportedTypeVisibilityInProgress.Remove(handle);
            }
        }

        private string GetMostVisibleAccessibility(IEnumerable<MethodDefinitionHandle> handles)
        {
            var accessibilities = handles
                .Where(handle => !handle.IsNil)
                .Select(handle => reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask)
                .Where(static access => access is MethodAttributes.Public
                    or MethodAttributes.FamORAssem
                    or MethodAttributes.Family)
                .ToArray();
            if (accessibilities.Contains(MethodAttributes.Public))
                return "public";
            if (accessibilities.Contains(MethodAttributes.FamORAssem))
                return "protected internal";
            return "protected";
        }

        private static string GetExportedTypeAccessibility(ExportedType type)
        {
            if (type.IsForwarder && type.Implementation.Kind == HandleKind.AssemblyReference)
                return "public";
            return GetTypeAccessibility(type.Attributes);
        }

        private static string GetTypeAccessibility(TypeAttributes attributes) =>
            (attributes & TypeAttributes.VisibilityMask) switch
            {
                TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
                TypeAttributes.NestedFamily => "protected",
                TypeAttributes.NestedFamORAssem => "protected internal",
                _ => throw new BadImageFormatException("A non-public type reached the public API formatter.")
            };

        private static string GetMethodAccessibility(MethodAttributes attributes) =>
            (attributes & MethodAttributes.MemberAccessMask) switch
            {
                MethodAttributes.Public => "public",
                MethodAttributes.Family => "protected",
                MethodAttributes.FamORAssem => "protected internal",
                _ => throw new BadImageFormatException("A non-public method reached the public API formatter.")
            };

        private static string GetFieldAccessibility(FieldAttributes attributes) =>
            (attributes & FieldAttributes.FieldAccessMask) switch
            {
                FieldAttributes.Public => "public",
                FieldAttributes.Family => "protected",
                FieldAttributes.FamORAssem => "protected internal",
                _ => throw new BadImageFormatException("A non-public field reached the public API formatter.")
            };

        private static string FormatTypeModifiers(TypeAttributes attributes)
        {
            var modifiers = new List<string>();
            if ((attributes & TypeAttributes.Abstract) != 0)
                modifiers.Add("abstract");
            if ((attributes & TypeAttributes.Sealed) != 0)
                modifiers.Add("sealed");
            if ((attributes & TypeAttributes.BeforeFieldInit) != 0)
                modifiers.Add("beforefieldinit");
            modifiers.Add((attributes & TypeAttributes.LayoutMask) switch
            {
                TypeAttributes.SequentialLayout => "sequential-layout",
                TypeAttributes.ExplicitLayout => "explicit-layout",
                _ => "auto-layout"
            });
            return string.Join(' ', modifiers) + " ";
        }

        private static string FormatMethodModifiers(
            MethodAttributes attributes,
            bool includeSpecialName = true)
        {
            var modifiers = new List<string>();
            if ((attributes & MethodAttributes.Static) != 0)
                modifiers.Add("static");
            if ((attributes & MethodAttributes.Abstract) != 0)
                modifiers.Add("abstract");
            if ((attributes & MethodAttributes.Virtual) != 0)
                modifiers.Add("virtual");
            if ((attributes & MethodAttributes.Final) != 0)
                modifiers.Add("final");
            if ((attributes & MethodAttributes.NewSlot) != 0)
                modifiers.Add("newslot");
            if ((attributes & MethodAttributes.PinvokeImpl) != 0)
                modifiers.Add("pinvoke");
            if (includeSpecialName && (attributes & MethodAttributes.SpecialName) != 0)
                modifiers.Add("specialname");
            return modifiers.Count == 0 ? string.Empty : string.Join(' ', modifiers) + " ";
        }

        private static void AddAccessorHandles(
            ISet<MethodDefinitionHandle> handles,
            MethodDefinitionHandle first,
            MethodDefinitionHandle second,
            ImmutableArray<MethodDefinitionHandle> others)
        {
            if (!first.IsNil)
                handles.Add(first);
            if (!second.IsNil)
                handles.Add(second);
            foreach (var other in others)
                handles.Add(other);
        }

        private static void AddAccessorHandles(
            ISet<MethodDefinitionHandle> handles,
            MethodDefinitionHandle first,
            MethodDefinitionHandle second,
            MethodDefinitionHandle third,
            ImmutableArray<MethodDefinitionHandle> others)
        {
            AddAccessorHandles(handles, first, second, others);
            if (!third.IsNil)
                handles.Add(third);
        }

        private static IEnumerable<MethodDefinitionHandle> EnumerateAccessorHandles(
            MethodDefinitionHandle first,
            MethodDefinitionHandle second,
            ImmutableArray<MethodDefinitionHandle> others)
        {
            if (!first.IsNil)
                yield return first;
            if (!second.IsNil)
                yield return second;
            foreach (var other in others)
                yield return other;
        }

        private static IEnumerable<MethodDefinitionHandle> EnumerateAccessorHandles(
            MethodDefinitionHandle first,
            MethodDefinitionHandle second,
            MethodDefinitionHandle third,
            ImmutableArray<MethodDefinitionHandle> others)
        {
            foreach (var handle in EnumerateAccessorHandles(first, second, others))
                yield return handle;
            if (!third.IsNil)
                yield return third;
        }

        private static string EscapeName(string name)
        {
            if (name.Length > 0 && name.All(static character =>
                    char.IsLetterOrDigit(character) || character is '_' or '.' or '`' or '<' or '>'))
            {
                return name;
            }

            return $"name({Quote(name)})";
        }

        private static string FormatChar(char value) => value switch
        {
            '\\' => "'\\\\'",
            '\'' => "'\\\''",
            '\r' => "'\\r'",
            '\n' => "'\\n'",
            '\t' => "'\\t'",
            _ when char.IsControl(value) => $"'\\u{(int)value:x4}'",
            _ => $"'{value}'"
        };
    }
}
