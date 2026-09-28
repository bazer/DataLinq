using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DataLinq;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using PackedGenerated;

internal static class ContractManifest
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private static readonly NullabilityInfoContext Nullability = new();
    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    private static bool Async(MethodInfo method) => method.Name.Contains("Async", StringComparison.Ordinal);
    private static bool IncludedMethod(MethodInfo method) => Async(method) ||
        method.DeclaringType == typeof(DataLinq.Diagnostics.DataLinqFailure) || method.Name == "AsKeyValuePairs";

    internal static void Write(string path)
    {
        CheckCount(typeof(DataLinqAsyncQueryableExtensions), 41);
        CheckCount(typeof(IImmutableRelation<>), 46);
        CheckCount(typeof(ImmutableRelation<,>), 46);
        CheckCount(typeof(ImmutableRelationMock<>), 46);
        CheckCount(typeof(IDatabaseAccess), 10);
        CheckCount(typeof(DatabaseAccess), 10);
        CheckCount(typeof(DataLinq.Query.Select<>), 9);
        CheckCount(typeof(ChildExtensions), 19);
        foreach (var type in new[] { typeof(DataLinqAsyncQueryableExtensions), typeof(IImmutableRelation<>) })
        {
            foreach (var name in new[] { "SumAsync", "AverageAsync" })
                if (type.GetMethods(Declared).Count(method => method.Name == name) != 10) throw new Exception($"Numeric overload drift: {type}/{name}.");
        }
        var disposal = typeof(IDatabaseProvider).GetMethods(Declared).Single(method => method.Name.EndsWith(".DisposeAsync", StringComparison.Ordinal));
        if (!disposal.IsPrivate || disposal.IsAbstract || !typeof(IAsyncDisposable).IsAssignableFrom(typeof(IDatabaseProvider)))
            throw new Exception("Inherited default disposal slot changed.");
        var bridge = typeof(Mutable<>).GetMethod("ExecuteGeneratedMutationAsync", Declared)!;
        if (!bridge.IsFamily || !bridge.IsStatic || bridge.GetParameters().Any(parameter => parameter.IsOptional))
            throw new Exception("Approved protected bridge shape changed.");
        var assemblies = new[] {
            typeof(Database<>).Assembly, typeof(DataLinq.SQLite.SQLiteProvider<>).Assembly,
            typeof(DataLinq.MySql.MySqlProvider<>).Assembly, typeof(DataLinq.Memory.MemoryDatabase<>).Assembly,
            typeof(AdvancedDatabase).Assembly
        };
        var types = assemblies.SelectMany(assembly => assembly.GetExportedTypes()).Where(type =>
            type.Namespace == "PackedGenerated" ||
            type == typeof(DatabaseProvider<>) || type == typeof(DataLinq.MySql.MySqlProvider<>) ||
            type == typeof(DataLinq.MariaDB.MariaDBProvider<>) || type == typeof(IImmutableForeignKey<>) ||
            type == typeof(DataLinqExecutionOptions) || type.Name.StartsWith("DataLinqFailure", StringComparison.Ordinal) ||
            type.Name is "DataLinqSecondaryFailure" or "DataLinqOperationKind" or "DataLinqCompletionOutcome" or "DataLinqRecoveryActions" ||
            type.GetMethods(Declared).Any(method => Async(method) && (Visible(method) || type.IsInterface)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        if (assemblies[0].GetType("DataLinq.Validation.DataLinqSchemaValidator") is not null)
            throw new Exception("W5 validation unexpectedly entered the W3 candidate.");
        var manifest = new {
            SchemaVersion = "w3.compiled-contract-manifest.v1",
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Assemblies = assemblies.Select(assembly => new { assembly.FullName, assembly.ManifestModule.ModuleVersionId,
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant() }),
            Types = types.Select(type => new {
                Name = TypeName(type), Attributes = type.Attributes.ToString(), MetadataAttributes = Attributes(type.CustomAttributes),
                BaseType = type.BaseType is null ? null : TypeName(type.BaseType),
                Interfaces = type.GetInterfaces().Select(TypeName).OrderBy(name => name, StringComparer.Ordinal),
                GenericParameters = GenericParameters(type.IsGenericTypeDefinition ? type.GetGenericArguments() : []),
                Constructors = type.GetConstructors(Declared).Where(Visible).Select(constructor => new {
                    Attributes = constructor.Attributes.ToString(), Parameters = constructor.GetParameters().Select(Parameter) }),
                Methods = type.GetMethods(Declared).Where(method => IncludedMethod(method) && (Visible(method) || type.IsInterface))
                    .OrderBy(method => method.ToString(), StringComparer.Ordinal).Select(method => new {
                        method.Name, Attributes = method.Attributes.ToString(), MetadataAttributes = Attributes(method.CustomAttributes),
                        Return = Parameter(method.ReturnParameter), GenericParameters = GenericParameters(method.GetGenericArguments()),
                        Parameters = method.GetParameters().Select(Parameter), HasBody = method.GetMethodBody() is not null }),
                Properties = type.GetProperties(Declared).Where(property => property.GetAccessors(true).Any(Visible))
                    .OrderBy(property => property.Name, StringComparer.Ordinal).Select(property => new {
                        property.Name, Type = TypeName(property.PropertyType), Nullability = NullabilityShape(Nullability.Create(property)),
                        Getter = property.GetMethod?.Attributes.ToString(), Setter = property.SetMethod?.Attributes.ToString(),
                        SetterReturnModifiers = property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Select(TypeName),
                        MetadataAttributes = Attributes(property.CustomAttributes) }),
                EnumValues = type.IsEnum ? Enum.GetNames(type).Select(name => new { Name = name, Value = Convert.ToInt64(Enum.Parse(type, name)) }).ToArray() : null
            })
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CheckCount(Type type, int expected)
    {
        var actual = type.GetMethods(Declared).Count(method => method.IsPublic && Async(method));
        if (actual != expected) throw new Exception($"Compiled async contract drift: {type} expected {expected}, got {actual}.");
    }
    private static string TypeName(Type type) => type.IsGenericParameter ? type.Name :
        type.IsArray ? TypeName(type.GetElementType()!) + "[]" :
        type.IsGenericType ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">" : type.FullName ?? type.Name;
    private static object Parameter(ParameterInfo parameter) => new {
        parameter.Name, Type = TypeName(parameter.ParameterType), Attributes = parameter.Attributes.ToString(),
        parameter.IsOptional, parameter.HasDefaultValue,
        DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue?.ToString() : null,
        Nullability = NullabilityShape(Nullability.Create(parameter)), MetadataAttributes = Attributes(parameter.CustomAttributes)
    };
    private static object NullabilityShape(NullabilityInfo info) => new {
        Read = info.ReadState.ToString(), Write = info.WriteState.ToString(),
        GenericArguments = info.GenericTypeArguments.Select(NullabilityShape),
        Element = info.ElementType is null ? null : NullabilityShape(info.ElementType)
    };
    private static object GenericParameters(Type[] arguments) => arguments.Select(type => new {
        type.Name, Attributes = type.GenericParameterAttributes.ToString(), Constraints = type.GetGenericParameterConstraints().Select(TypeName),
        MetadataAttributes = Attributes(type.CustomAttributes)
    });
    private static string[] Attributes(IEnumerable<CustomAttributeData> attributes) => attributes.Select(attribute => attribute.ToString())
        .OrderBy(value => value, StringComparer.Ordinal).ToArray();
}
