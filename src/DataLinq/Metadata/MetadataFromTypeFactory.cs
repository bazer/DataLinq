using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Interfaces;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Metadata;

public static class MetadataFromTypeFactory
{
    private const string GeneratedMetadataMethodName = "GetDataLinqGeneratedMetadata";
    private const string GeneratedMetadataBindingMethodName = "SetDataLinqGeneratedMetadata";

    public static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromDatabaseModel<TDatabase>()
        where TDatabase : class, IDatabaseModel<TDatabase> =>
        BuildGeneratedMetadata(
            typeof(TDatabase),
            () => TDatabase.GetDataLinqGeneratedMetadata(),
            TDatabase.SetDataLinqGeneratedMetadata);

    [RequiresUnreferencedCode("Non-generic generated metadata loading reflects over the generated hook. Provider startup uses the static generic hook and does not require this path.")]
    public static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseFromDatabaseModel(Type type)
    {
        if (type is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Database type cannot be null.");

        var draftResult = GetGeneratedMetadataDraft(type);
        if (!draftResult.TryUnwrap(out var draft, out var draftFailure))
            return draftFailure;

        return BuildGeneratedMetadataDraft(type, draft, static _ => null);
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildGeneratedMetadata(
        Type databaseType,
        Func<MetadataDatabaseDraft> getMetadataDraft,
        Action<DatabaseDefinition> bindMetadata)
    {
        if (databaseType is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Database type cannot be null.");

        MetadataDatabaseDraft draft;
        try
        {
            draft = getMetadataDraft();
        }
        catch (Exception exception)
        {
            return CreateUnreadableGeneratedMetadataFailure(databaseType, exception);
        }

        return BuildGeneratedMetadataDraft(
            databaseType,
            draft,
            metadata =>
            {
                bindMetadata(metadata);
                return null;
            });
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildGeneratedMetadataDraft(
        Type databaseType,
        MetadataDatabaseDraft draft,
        Func<DatabaseDefinition, IDLOptionFailure?> bindMetadata)
    {
        var hookName = GetHookName(databaseType);
        if (draft is null)
            return CreateNullGeneratedMetadataPayloadFailure(databaseType);

        var metadataResult = new MetadataDefinitionFactory().Build(draft);
        if (!metadataResult.TryUnwrap(out var metadata, out var failure))
        {
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Generated DataLinq metadata hook '{hookName}' returned invalid metadata for database '{GetDatabaseTypeName(databaseType)}'. Regenerate the DataLinq model sources with the current generator package.",
                [failure]);
        }

        try
        {
            var bindingFailure = bindMetadata(metadata);
            if (bindingFailure is not null)
                return bindingFailure;
        }
        catch (Exception exception)
        {
            return CreateUnreadableGeneratedMetadataBindingFailure(databaseType, exception);
        }

        return metadata;
    }

    [RequiresUnreferencedCode("Uses reflection to invoke the generated metadata hook for non-generic callers.")]
    private static Option<MetadataDatabaseDraft, IDLOptionFailure> GetGeneratedMetadataDraft(Type databaseType)
    {
        if (databaseType is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Database type cannot be null.");

        var generatedMetadataMethod = databaseType.GetMethod(
            GeneratedMetadataMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        var hookName = GetHookName(databaseType);
        if (generatedMetadataMethod is null)
        {
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Database model '{GetDatabaseTypeName(databaseType)}' is missing the generated complete DataLinq metadata hook '{GeneratedMetadataMethodName}'. Regenerate the DataLinq model sources with the current generator package and ensure the database model is declared as a partial class.");
        }

        if (generatedMetadataMethod.ReturnType != typeof(MetadataDatabaseDraft))
        {
            return DLOptionFailure.Fail(
                DLFailureType.InvalidModel,
                $"Generated DataLinq metadata hook '{hookName}' must return '{typeof(MetadataDatabaseDraft).FullName}'. Regenerate the DataLinq model sources with the current generator package.");
        }

        try
        {
            var draft = generatedMetadataMethod.Invoke(null, null);
            if (draft is null)
                return CreateNullGeneratedMetadataPayloadFailure(databaseType);

            return (MetadataDatabaseDraft)draft;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return CreateUnreadableGeneratedMetadataFailure(databaseType, exception.InnerException);
        }
        catch (Exception exception)
        {
            return CreateUnreadableGeneratedMetadataFailure(databaseType, exception);
        }
    }

    private static IDLOptionFailure CreateUnreadableGeneratedMetadataFailure(Type databaseType, Exception exception) =>
        DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"Generated DataLinq metadata hook '{GetHookName(databaseType)}' could not be read for database '{GetDatabaseTypeName(databaseType)}'. {exception.GetType().Name}: {exception.Message}. Regenerate the DataLinq model sources with the current generator package.");

    private static IDLOptionFailure CreateUnreadableGeneratedMetadataBindingFailure(Type databaseType, Exception exception) =>
        DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"Generated DataLinq metadata binding hook '{GetBindingHookName(databaseType)}' could not bind runtime metadata for database '{GetDatabaseTypeName(databaseType)}'. {exception.GetType().Name}: {exception.Message}. Regenerate the DataLinq model sources with the current generator package.");

    private static IDLOptionFailure CreateNullGeneratedMetadataPayloadFailure(Type databaseType) =>
        DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"Generated DataLinq metadata hook '{GetHookName(databaseType)}' returned a null metadata payload for database '{GetDatabaseTypeName(databaseType)}'. Regenerate the DataLinq model sources with the current generator package.");

    private static string GetHookName(Type databaseType) =>
        $"{GetDatabaseTypeName(databaseType)}.{GeneratedMetadataMethodName}";

    private static string GetBindingHookName(Type databaseType) =>
        $"{GetDatabaseTypeName(databaseType)}.{GeneratedMetadataBindingMethodName}";

    private static string GetDatabaseTypeName(Type databaseType) =>
        databaseType.FullName ?? databaseType.Name;
}
