using System;
using System.ComponentModel;

namespace DataLinq.Instances;

/// <summary>
/// Runtime helpers called by generated mutable-model default initializers.
/// This type is public because generated code is compiled into the consumer assembly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedDefaultValueFactory
{
    /// <summary>
    /// Creates an RFC 9562 UUID version 7 value on every supported DataLinq target.
    /// </summary>
    public static Guid CreateVersion7Guid() =>
        CreateVersion7Guid(DateTimeOffset.UtcNow, Guid.NewGuid());

    internal static Guid CreateVersion7Guid(DateTimeOffset timestamp, Guid entropy)
    {
        var unixTimeMilliseconds = timestamp.ToUnixTimeMilliseconds();
        if (unixTimeMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp, "UUID version 7 timestamps cannot precede the Unix epoch.");

        Span<byte> bytes = stackalloc byte[16];
        _ = entropy.TryWriteBytes(bytes, bigEndian: true, out _);

        // RFC 9562 stores the 48-bit Unix millisecond timestamp in network byte order.
        bytes[0] = (byte)(unixTimeMilliseconds >> 40);
        bytes[1] = (byte)(unixTimeMilliseconds >> 32);
        bytes[2] = (byte)(unixTimeMilliseconds >> 24);
        bytes[3] = (byte)(unixTimeMilliseconds >> 16);
        bytes[4] = (byte)(unixTimeMilliseconds >> 8);
        bytes[5] = (byte)unixTimeMilliseconds;

        // Preserve 74 random bits while setting version 7 and the RFC 4122/9562 variant.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
