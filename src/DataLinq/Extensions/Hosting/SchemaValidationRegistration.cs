using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Extensions.Hosting;

internal sealed record SchemaValidationSelection(Type ModelType, object? Key, IDataLinqRegistration? Attached,
    Action<DataLinqSchemaStartupOptions>? Configure);
internal sealed record CapturedStartupTarget(IDataLinqRegistration Registration, DataLinqSchemaValidationTarget Identity,
    DataLinqSchemaStartupMode Mode, CapturedSchemaValidationOptions Validation);

internal sealed class SchemaValidationRegistration
{
    internal List<Action<DataLinqSchemaStartupOptions>> Defaults { get; } = [];
    internal List<SchemaValidationSelection> Selections { get; } = [];

    internal IReadOnlyList<CapturedStartupTarget> Capture(IServiceProvider services)
    {
        var callbacks = Defaults.ToArray();
        var selections = Selections.ToArray();
        var defaults = new DataLinqSchemaStartupOptions();
        foreach (var configure in callbacks) configure(defaults);
        _ = defaults.Capture();
        defaults = defaults.Copy();
        if (defaults.Mode == DataLinqSchemaStartupMode.Disabled) return Array.Empty<CapturedStartupTarget>();
        if (selections.Length == 0) throw new InvalidOperationException("Enabled DataLinq schema validation requires at least one selected registration.");

        var registrations = services.GetServices<IDataLinqRegistration>().ToArray();
        var canonical = new List<(IDataLinqRegistration Registration, Action<DataLinqSchemaStartupOptions>? Configure)>();
        foreach (var selection in selections)
        {
            var matches = registrations.Where(registration => selection.Attached is not null
                ? ReferenceEquals(registration, selection.Attached)
                : registration.ModelType == selection.ModelType && (selection.Key is null ? registration.IsDefault : Equals(selection.Key, registration.Key))).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Startup validation selection for model '{selection.ModelType.FullName}' does not resolve to exactly one DataLinq registration. Select an explicit key or register an unnamed/default target.");
            var existing = canonical.FindIndex(item => ReferenceEquals(item.Registration, matches[0]));
            if (existing < 0) canonical.Add((matches[0], selection.Configure));
            else if (selection.Configure is not null)
            {
                if (canonical[existing].Configure is not null)
                    throw new InvalidOperationException($"Startup validation for model '{selection.ModelType.FullName}' has more than one explicit target configuration. Configure its canonical registration once.");
                canonical[existing] = (matches[0], selection.Configure);
            }
        }

        var captured = new List<CapturedStartupTarget>();
        foreach (var item in canonical)
        {
            var options = defaults.Copy();
            item.Configure?.Invoke(options);
            var validation = options.Capture();
            captured.Add(new(item.Registration, new(item.Registration.ModelType, item.Registration.Key, captured.Count + 1), options.Mode, validation));
        }
        return captured.AsReadOnly();
    }
}
