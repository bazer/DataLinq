using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Validation;

namespace DataLinq.MySql;

public abstract partial class MetadataFromSqlFactory
{
    private SchemaReadLimitations? limitations;

    private void ReportLimitation(string? table, string? name, SchemaReadAspect aspect, SchemaReadLimitationReason reason, string message)
    {
        limitations?.Add(new(table, name, aspect, reason, message));
        options.Log?.Invoke(message);
    }

    private void ReportForeignKeyLimitation(NativeKey key, IReadOnlyList<NativeKey> keys, SchemaIdentifierRules? identifiers,
        SchemaReadLimitationReason reason, string message)
    {
        if (limitations is not null && identifiers is not null)
        {
            var group = keys.Where(part => identifiers.TableNames.Equals(part.Table, key.Table) &&
                identifiers.ConstraintNames.Equals(part.Constraint, key.Constraint)).OrderBy(part => part.Ordinal).ToArray();
            limitations.Add(new(key.Table, key.Constraint, SchemaReadAspect.ForeignKey, reason, message,
                group.All(part => part.Column is not null) ? group.Select(part => part.Column!).ToArray() : null,
                key.ReferencedTable, group.All(part => part.ReferencedColumn is not null) ? group.Select(part => part.ReferencedColumn!).ToArray() : null));
        }
        options.Log?.Invoke(message);
    }

    private bool ReportExternalReference(ProviderDatabaseDraft database, NativeKey key, SchemaIdentifierRules? identifiers)
    {
        if (identifiers is null) return false;
        if (string.IsNullOrWhiteSpace(key.ReferencedSchema))
            throw new InvalidOperationException($"Foreign key '{key.Constraint}' on table '{key.Table}' has no referenced database identity.");
        if (identifiers.DatabaseNames.Equals(database.DbName, key.ReferencedSchema)) return false;
        ReportLimitation(key.Table, key.Constraint, SchemaReadAspect.ForeignKey, SchemaReadLimitationReason.ExternalForeignKeyTarget,
            $"Warning: Skipping foreign key '{key.Constraint}' on table '{key.Table}' because referenced database object '{key.ReferencedSchema}.{key.ReferencedTable}.{key.ReferencedColumn}' is outside the observed database.");
        return true;
    }

    private static void ValidateRuntimeKeyCompleteness(ProviderDatabaseDraft database, IReadOnlyList<NativeKey> keys,
        SchemaIdentifierRules? identifiers, List<IDLOptionFailure> failures)
    {
        if (identifiers is null) return;
        foreach (var group in keys.GroupBy(key => (key.Table, key.Constraint), new CatalogObjectComparer(identifiers, identifiers.ConstraintNames)))
        {
            if (group.Count() < 2 || group.Any(key => string.IsNullOrWhiteSpace(key.Table) || string.IsNullOrWhiteSpace(key.Column) ||
                string.IsNullOrWhiteSpace(key.ReferencedTable) || string.IsNullOrWhiteSpace(key.ReferencedColumn) ||
                !identifiers.DatabaseNames.Equals(key.ReferencedSchema, database.DbName))) continue;
            var represented = group.Count(key => database.TableModels.Any(source => source.Table.DbName == key.Table &&
                source.Table.Columns.Any(column => column.Column.DbName == key.Column)) &&
                database.TableModels.Any(target => target.Table.DbName == key.ReferencedTable &&
                    target.Table.Columns.Any(column => column.Column.DbName == key.ReferencedColumn)));
            if (represented != 0 && represented != group.Count())
                failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel,
                    $"Foreign key '{group.First().Constraint}' on table '{group.First().Table}' is only partially representable. A complete relationship cannot be acquired because one or more columns were not imported."));
        }
    }
}
