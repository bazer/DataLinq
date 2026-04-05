using System.Collections.Generic;

namespace DataLinq.Testing.CLI;

internal sealed record TestInfraRuntimeState(
    int Version,
    string? AliasName,
    string Host,
    string AdminUser,
    string AdminPassword,
    string ApplicationUser,
    string ApplicationPassword,
    IReadOnlyList<TestInfraRuntimeTargetState> Targets);

internal sealed record TestInfraRuntimeTargetState(
    string Id,
    string Runtime,
    int? Port);
