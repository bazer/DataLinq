using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length < 2) throw new ArgumentException("Supply an output report and at least one payload to inspect.");
var rows = new List<object>();
var tokens = new[] { "DataLinq.MySql", "DataLinq.SQLite" };
foreach (var path in args.Skip(1))
{
    var original = File.ReadAllBytes(path);
    var bytes = original;
    if (path.EndsWith(".br") || path.EndsWith(".gz"))
    {
        using var input = new MemoryStream(bytes);
        using Stream decompressor = path.EndsWith(".br") ? new BrotliStream(input, CompressionMode.Decompress) : new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output); bytes = output.ToArray();
    }
    var references = new List<string>();
    var friends = new List<string>();
    var isPe = bytes.Length > 2 && bytes[0] == 'M' && bytes[1] == 'Z';
    if (isPe)
    {
        using var pe = new PEReader(new MemoryStream(bytes));
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.AssemblyReferences)
            references.Add(metadata.GetString(metadata.GetAssemblyReference(handle).Name));
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (metadata.GetString(type.Namespace) != "System.Runtime.CompilerServices" || metadata.GetString(type.Name) != "InternalsVisibleToAttribute") continue;
            var blob = metadata.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new Exception("Invalid attribute prolog.");
            friends.Add(blob.ReadSerializedString()!);
        }
        if (references.Any(reference => tokens.Contains(reference))) throw new Exception("Actual SQL provider assembly reference found.");
    }
    var matches = tokens.Select(token => {
        var utf8 = Encoding.UTF8.GetBytes(token);
        var attributeBlob = new byte[] { 1, 0, (byte)utf8.Length }.Concat(utf8).Concat(new byte[] { 0, 0 }).ToArray();
        var count = Count(bytes, utf8);
        var friendCount = Count(bytes, attributeBlob);
        var utf16Count = Count(bytes, Encoding.Unicode.GetBytes(token));
        if (count != 1 || friendCount != count || utf16Count != 0 || isPe && !friends.Contains(token))
            throw new Exception($"Token is not confined to the expected friend declaration: {path}/{token}.");
        return new { Token = token, Utf8Occurrences = count, Utf16Occurrences = utf16Count, ExactFriendAttributeBlobOccurrences = friendCount };
    }).ToArray();
    rows.Add(new { Path = Path.GetFullPath(path), Sha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
        IsManagedPe = isPe, AssemblyReferences = references, InternalsVisibleTo = friends, Matches = matches });
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = true, Note = "PE references and actual friend attributes decoded; WebCIL/compressed payloads checked for only the same exact serialized friend-attribute blobs.", Files = rows }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Verified {rows.Count} payloads: each SQL-provider name occurs only once, in its friend declaration; decoded PE references contain neither provider.");

static int Count(byte[] bytes, byte[] needle)
{
    var count = 0;
    for (var i = 0; i <= bytes.Length - needle.Length; i++)
        if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle)) count++;
    return count;
}
