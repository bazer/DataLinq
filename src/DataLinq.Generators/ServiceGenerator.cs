using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DataLinq.SourceGenerators;

internal static class ServiceGenerator
{
    private const string Interfaces = "DataLinq.Interfaces";
    private const string Attributes = "DataLinq.Attributes";
    private static readonly SymbolDisplayFormat Types = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var services = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
            static (input, token) => input.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)input.Node, token))
            .Where(static symbol => symbol is not null && symbol.Interfaces.Any(type => Is(type, Interfaces, "IDataLinqService`1")))
            .Collect();
        context.RegisterSourceOutputSafely(services, static (output, candidates) =>
        {
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var candidate in candidates)
                if (candidate is not null && seen.Add(candidate)) Emit(output, candidate);
        }, "DataLinqServiceGenerator");
    }

    private static void Emit(SourceProductionContext output, INamedTypeSymbol service)
    {
        var failed = false;
        void Error(ISymbol symbol, string reason)
        {
            failed = true;
            output.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.InvalidService,
                symbol.Locations.FirstOrDefault(), service.Name, reason));
        }
        var declarations = service.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(output.CancellationToken)).OfType<ClassDeclarationSyntax>().ToArray();
        if (declarations.Any(declaration => !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            Error(service, "the service must be partial");
        if (service.IsAbstract || service.IsStatic || service.IsGenericType || service.ContainingType is not null || service.IsFileLocal)
            Error(service, "use a concrete, non-generic, top-level class (file-local services are unsupported)");
        var models = service.Interfaces.Where(type => Is(type, Interfaces, "IDataLinqService`1")).ToArray();
        if (models.Length != 1) { Error(service, "select exactly one database model"); return; }
        var model = models[0].TypeArguments[0];
        bool Factory(ITypeSymbol type) => IsFor(type, "IDataLinqUnitOfWorkFactory`1", model);
        var constructors = service.InstanceConstructors.Where(constructor => !constructor.IsImplicitlyDeclared && constructor.Parameters.Any(parameter => Factory(parameter.Type))).ToArray();
        if (constructors.Length != 1 || constructors[0].Parameters.Count(parameter => Factory(parameter.Type)) != 1)
        {
            Error(service, "exactly one constructor must accept exactly one matching IDataLinqUnitOfWorkFactory parameter");
            return;
        }
        var constructor = constructors[0];
        if (constructor.Parameters.Any(parameter => parameter.RefKind != RefKind.None || UnsupportedType(parameter.Type)))
            Error(constructor, "constructor dependencies cannot use ref/out/in, pointers or ref-like types");
        var primary = declarations.SingleOrDefault(declaration => declaration.ParameterList is not null);
        var selectedMembers = service.GetMembers().Where(member => HasAttribute(member, "DataLinqFactoryAttribute")).ToArray();
        string factory;
        var factoryParameter = constructor.Parameters.Single(parameter => Factory(parameter.Type));
        var generatedFactory = "__DataLinqFactory";
        while (service.GetMembers(generatedFactory).Length != 0) generatedFactory += "_";
        if (primary is not null)
        {
            if (selectedMembers.Length != 0 || !constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(output.CancellationToken) is ClassDeclarationSyntax))
                Error(service, "a primary constructor must supply the matching factory without a separate DataLinqFactory designation");
            factory = "this." + generatedFactory;
        }
        else
        {
            var selected = selectedMembers.SingleOrDefaultSafe();
            var selectedType = selected switch { IFieldSymbol field => field.Type, IPropertySymbol property => property.Type, _ => null };
            if (selected is null || selected.IsStatic || selectedType is null || !Factory(selectedType) ||
                selected is IPropertySymbol { GetMethod: null } or IPropertySymbol { IsIndexer: true })
            {
                Error(service, "a traditional constructor requires one readable instance factory field/property marked DataLinqFactory");
                return;
            }
            factory = "this.@" + selected.Name;
        }
        var operations = new List<Operation>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in service.GetMembers().OfType<IMethodSymbol>().Where(method => HasAttribute(method, "DataLinqOperationAttribute")))
        {
            var task = Is(method.ReturnType, "System.Threading.Tasks", "Task") || Is(method.ReturnType, "System.Threading.Tasks", "Task`1");
            var suffix = task ? "CoreAsync" : "Core";
            var name = method.Name.EndsWith(suffix, StringComparison.Ordinal) ? method.Name.Substring(0, method.Name.Length - suffix.Length) + (task ? "Async" : "") : "";
            if (name.Length == 0 || name == "Async") { Error(method, $"operation '{method.Name}' must use a non-empty name ending in {suffix}"); continue; }
            if (service.GetMembers(name).Length != 0 || !names.Add(name)) { Error(method, $"generated entry-point name '{name}' is already used"); continue; }
            if (method.DeclaredAccessibility != Accessibility.Private || method.IsStatic || method.IsAbstract || method.IsGenericMethod ||
                method.ReturnsByRef || method.ReturnsByRefReadonly || method.IsVararg || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || UnsupportedType(parameter.Type)) || UnsupportedType(method.ReturnType))
            {
                Error(method, $"operation '{method.Name}' must be a private instance method without generic, ref/out/in, pointer or ref-like signatures");
                continue;
            }
            if (method.IsAsync && method.ReturnsVoid || !task && method.ReturnType is INamedTypeSymbol awaitable &&
                (Is(awaitable, "System.Threading.Tasks", "ValueTask") || Is(awaitable, "System.Threading.Tasks", "ValueTask`1") || awaitable.GetMembers("GetAwaiter").Length != 0))
            {
                Error(method, $"operation '{method.Name}' must return void, an ordinary value, Task or Task<T>; async void and other awaitables are unsupported");
                continue;
            }
            if (method.Parameters.Length == 0 || !IsFor(method.Parameters[0].Type, "IDataLinqSession`1", model) ||
                method.Parameters.Skip(1).Any(parameter => Is(parameter.Type, Interfaces, "IDataLinqSession`1")))
            {
                Error(method, $"operation '{method.Name}' must begin with exactly one IDataLinqSession for its service's database model");
                continue;
            }
            if (task && (method.Parameters.Length < 2 || !Is(method.Parameters.Last().Type, "System.Threading", "CancellationToken") ||
                method.Parameters.Count(parameter => Is(parameter.Type, "System.Threading", "CancellationToken")) != 1))
            {
                Error(method, $"async operation '{method.Name}' must end with one explicit CancellationToken parameter");
                continue;
            }
            if (method.Parameters.Any(parameter => parameter.GetAttributes().Length != 0))
            {
                Error(method, $"operation '{method.Name}' parameter attributes are unsupported on generated entry points");
                continue;
            }
            var attribute = method.GetAttributes().Single(attribute => Is(attribute.AttributeClass, Attributes, "DataLinqOperationAttribute"));
            var resultAware = attribute.NamedArguments.Any(argument => argument.Key == "ResultAware" && argument.Value.Value is true);
            var payload = task && method.ReturnType is INamedTypeSymbol { Arity: 1 } taskType ? taskType.TypeArguments[0] : method.ReturnType;
            if (resultAware && !Is(payload, "DataLinq.Mutation", "TransactionResult`1") && !Is(payload, "DataLinq.Mutation", "TransactionResult`2"))
            {
                Error(method, $"result-aware operation '{method.Name}' must return TransactionResult<T> or TransactionResult<T, TFailure>, optionally in Task");
                continue;
            }
            operations.Add(new(method, name, task, resultAware));
        }
        foreach (var parameter in constructor.Parameters.Where(parameter => !Factory(parameter.Type)))
            if (parameter.GetAttributes().Any(attribute => !Is(attribute.AttributeClass, "Microsoft.Extensions.DependencyInjection", "FromKeyedServicesAttribute")) ||
                parameter.GetAttributes().Any(attribute => Is(attribute.AttributeClass, "Microsoft.Extensions.DependencyInjection", "FromKeyedServicesAttribute") && attribute.ConstructorArguments.Length != 1))
                Error(parameter, "constructor dependency attributes support only an explicit FromKeyedServices key");
        if (failed) return;

        var database = Type(model);
        var self = Type(service);
        var bridge = $"global::DataLinq.Interfaces.IDataLinqServiceFactory<{database}, {self}>";
        var text = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!service.ContainingNamespace.IsGlobalNamespace) text.Append("namespace ").Append(service.ContainingNamespace.ToDisplayString()).AppendLine(";");
        text.Append("partial class @").Append(service.Name).Append(" : ").AppendLine(bridge).AppendLine("{");
        if (primary is not null)
            text.Append("    private global::DataLinq.Interfaces.IDataLinqUnitOfWorkFactory<").Append(database).Append("> ")
                .Append(generatedFactory).Append(" => @").Append(factoryParameter.Name).AppendLine(";");
        text.Append("    static ").Append(self).Append(' ').Append(bridge).Append(".Create(global::System.IServiceProvider __services, global::DataLinq.Interfaces.IDataLinqUnitOfWorkFactory<")
            .Append(database).Append("> __factory) => new ").Append(self).Append('(')
            .Append(string.Join(", ", constructor.Parameters.Select(parameter => Factory(parameter.Type) ? "__factory" : Dependency(parameter))))
            .AppendLine(");");
        foreach (var operation in operations) EmitOperation(text, operation, factory);
        text.AppendLine("}");
        var hint = service.ToDisplayString() + ".Service.g.cs";
        output.AddSource(hint, SourceText.From(text.ToString(), Encoding.UTF8));
    }

    private static void EmitOperation(StringBuilder text, Operation operation, string factory)
    {
        var method = operation.Method;
        var business = method.Parameters.Skip(1).ToArray();
        string Parameter(IParameterSymbol parameter) => (parameter.IsParams ? "params " : "") + Type(parameter.Type) + " @" + parameter.Name +
            (operation.Async && parameter.Equals(method.Parameters.Last(), SymbolEqualityComparer.Default) ? " = default" :
                parameter.HasExplicitDefaultValue ? " = " + Default(parameter) : "");
        var sessionName = UniqueName("__session", method);
        var tokenName = UniqueName("__token", method);
        var coreArguments = new List<string> { sessionName };
        coreArguments.AddRange(business.Select(parameter => operation.Async && SymbolEqualityComparer.Default.Equals(parameter, method.Parameters.Last()) ? tokenName : "@" + parameter.Name));
        var callback = (operation.Async ? $"({sessionName}, {tokenName})" : sessionName) + " => this.@" + method.Name + "(" + string.Join(", ", coreArguments) + ")";
        var execute = operation.ResultAware
            ? "global::DataLinq.Mutation.DataLinqUnitOfWorkFactoryExtensions.ExecuteResult" + (operation.Async ? "Async" : "") + "(" + factory + ", " + callback
            : factory + ".Execute" + (operation.Async ? "Async" : "") + "(" + callback;
        if (operation.Async) execute += ", cancellationToken: @" + method.Parameters.Last().Name;
        execute += ")";
        text.Append("    public ").Append(Type(method.ReturnType)).Append(" @").Append(operation.Name).Append('(')
            .Append(string.Join(", ", business.Select(Parameter))).Append(") => ").Append(execute).AppendLine(";");

        text.Append("    public ").Append(operation.Async ? "async " : "").Append(Type(method.ReturnType)).Append(" @").Append(operation.Name).Append('(')
            .Append(string.Join(", ", method.Parameters.Select(Parameter))).AppendLine(")\n    {");
        text.Append("        global::System.ArgumentNullException.ThrowIfNull(@").Append(method.Parameters[0].Name).AppendLine(");");
        if (operation.Async) text.Append("        @").Append(method.Parameters.Last().Name).AppendLine(".ThrowIfCancellationRequested();");
        text.Append("        @").Append(method.Parameters[0].Name).AppendLine(".ValidateParticipation();");
        var hasValue = !method.ReturnsVoid && (!operation.Async || ((INamedTypeSymbol)method.ReturnType).Arity != 0);
        text.Append("        ").Append(hasValue ? "return " : "").Append(operation.Async ? "await " : "")
            .Append("this.@").Append(method.Name).Append('(').Append(string.Join(", ", method.Parameters.Select(parameter => "@" + parameter.Name))).Append(')')
            .Append(operation.Async ? ".ConfigureAwait(false)" : "").AppendLine(";\n    }");
    }

    private static string Dependency(IParameterSymbol parameter)
    {
        var type = Type(parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        var keyed = parameter.GetAttributes().FirstOrDefault(attribute => Is(attribute.AttributeClass, "Microsoft.Extensions.DependencyInjection", "FromKeyedServicesAttribute"));
        var value = keyed is null
            ? parameter.HasExplicitDefaultValue ? "__services.GetService(typeof(" + type + "))"
                : "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(__services, typeof(" + type + "))"
            : "global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions." + (parameter.HasExplicitDefaultValue ? "GetKeyedService" : "GetRequiredKeyedService") + "(__services, typeof(" + type + "), " + keyed.ConstructorArguments[0].ToCSharpString() + ")";
        // A constant such as 3 is implicitly convertible to byte at a call site,
        // but boxing it first produces Int32 and makes the later unbox fail.
        if (parameter.HasExplicitDefaultValue) value += " ?? (object?)((" + Type(parameter.Type) + ")(" + Default(parameter) + "))";
        return "(" + Type(parameter.Type) + ")(" + value + ")!";
    }

    private static string Default(IParameterSymbol parameter)
    {
        if (parameter.ExplicitDefaultValue is null) return "default(" + Type(parameter.Type) + ")";
        var literal = parameter.ExplicitDefaultValue switch
        {
            float value when float.IsNaN(value) => "global::System.Single.NaN",
            float value when float.IsPositiveInfinity(value) => "global::System.Single.PositiveInfinity",
            float value when float.IsNegativeInfinity(value) => "global::System.Single.NegativeInfinity",
            float value when value == 0 && float.IsNegativeInfinity(1f / value) => "-0.0F",
            float value => value.ToString("R", CultureInfo.InvariantCulture) + "F",
            double value when double.IsNaN(value) => "global::System.Double.NaN",
            double value when double.IsPositiveInfinity(value) => "global::System.Double.PositiveInfinity",
            double value when double.IsNegativeInfinity(value) => "global::System.Double.NegativeInfinity",
            double value when value == 0 && double.IsNegativeInfinity(1d / value) => "-0.0D",
            double value => value.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal value => value.ToString(CultureInfo.InvariantCulture) + "M",
            uint value => value.ToString(CultureInfo.InvariantCulture) + "U",
            long value => value.ToString(CultureInfo.InvariantCulture) + "L",
            ulong value => value.ToString(CultureInfo.InvariantCulture) + "UL",
            var value => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)
                ?? throw new InvalidOperationException("Unsupported constructor or operation default constant.")
        };
        var constantType = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0] : parameter.Type;
        return constantType.TypeKind == TypeKind.Enum ? "(" + Type(constantType) + ")" + literal : literal;
    }
    private static string UniqueName(string name, IMethodSymbol method)
    {
        while (method.Parameters.Any(parameter => parameter.Name == name)) name += "_";
        return name;
    }
    private static string Type(ITypeSymbol type) => type.ToDisplayString(Types);
    private static bool UnsupportedType(ITypeSymbol type) => type.IsRefLikeType || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer;
    private static bool HasAttribute(ISymbol symbol, string name) => symbol.GetAttributes().Any(attribute => Is(attribute.AttributeClass, Attributes, name));
    private static bool Is(ITypeSymbol? type, string space, string name) => type is INamedTypeSymbol named && named.OriginalDefinition.MetadataName == name && named.ContainingNamespace.ToDisplayString() == space;
    private static bool IsFor(ITypeSymbol type, string name, ITypeSymbol model) => Is(type, Interfaces, name) && SymbolEqualityComparer.Default.Equals(((INamedTypeSymbol)type).TypeArguments[0], model);
    private static T? SingleOrDefaultSafe<T>(this T[] values) where T : class => values.Length == 1 ? values[0] : null;
    private sealed record Operation(IMethodSymbol Method, string Name, bool Async, bool ResultAware);
}
