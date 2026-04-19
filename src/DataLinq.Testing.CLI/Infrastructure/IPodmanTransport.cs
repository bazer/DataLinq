using System.Collections.Generic;

namespace DataLinq.Testing.CLI;

internal interface IPodmanTransport
{
    string Description { get; }

    PodmanCommandResult Execute(IReadOnlyList<string> arguments);
}
