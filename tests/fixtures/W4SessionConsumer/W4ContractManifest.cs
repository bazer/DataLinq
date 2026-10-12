using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DataLinq;
using DataLinq.Interfaces;
using SessionConsumer;

internal static class W4ContractManifest
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private static readonly NullabilityInfoContext Nullability = new();
    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    internal static void Write(string path)
    {
        var assemblies = new[] { typeof(Database<>).Assembly, typeof(DataLinq.SQLite.SQLiteDatabase<>).Assembly, typeof(TestDb).Assembly };
        var names = new[] { "DataLinq.Mutation.Transaction", "DataLinq.Mutation.Transaction`1", "DataLinq.Interfaces.IDataLinqSession`1",
            "DataLinq.Interfaces.IDataLinqUnitOfWork`1", "DataLinq.Interfaces.IDataLinqUnitOfWorkFactory`1",
            "DataLinq.Interfaces.IDataLinqService`1", "DataLinq.Interfaces.IDataLinqServiceFactory`2",
            "DataLinq.Attributes.DataLinqOperationAttribute", "DataLinq.Attributes.DataLinqFactoryAttribute",
            "DataLinq.Exceptions.DataLinqRollbackRequestedException", "DataLinq.Mutation.DataLinqUnitOfWorkFactory`1",
            "DataLinq.Mutation.DataLinqUnitOfWorkFactoryExtensions", "DataLinq.Mutation.TransactionResult",
            "DataLinq.Mutation.TransactionResult`1", "DataLinq.Mutation.TransactionResult`2", "DataLinq.Mutation.Success`1",
            "DataLinq.Mutation.Rollback`1", "DataLinq.Mutation.Unit", "SessionConsumer.TestRowExtensions",
            "SessionConsumer.BindingService", "SessionConsumer.TraditionalService", "DataLinq.SQLite.SQLiteDatabase`1" };
        var types = assemblies.SelectMany(assembly => assembly.GetExportedTypes()).Where(type =>
            names.Contains(type.FullName) || type.Namespace?.EndsWith("Extensions.DependencyInjection", StringComparison.Ordinal) == true)
            .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        var missing = names.Except(types.Select(type => type.FullName)).ToArray();
        if (missing.Length != 0) throw new Exception("The W4 contract inventory lost selected types: " + string.Join(", ", missing));
        var session = typeof(IDataLinqSession<>);
        if (session.GetInterfaces().Length != 0 || session.GetMethods().Any(method => method.Name is "Commit" or "Rollback" or "Dispose" or "DisposeAsync") ||
            session.GetProperties().Any(property => property.Name is "Provider" or "DatabaseAccess"))
            throw new Exception("Participant contract acquired ownership or raw provider access.");
        var manifest = new {
            SchemaVersion = "w4.compiled-contract-manifest.v1",
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
                Methods = type.GetMethods(Declared).Where(method => Visible(method) || type.IsInterface)
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
