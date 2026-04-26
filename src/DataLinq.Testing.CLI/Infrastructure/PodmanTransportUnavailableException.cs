using System;

namespace DataLinq.Testing.CLI;

internal sealed class PodmanTransportUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
}
