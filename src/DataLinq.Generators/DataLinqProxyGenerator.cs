//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using DataLinq.Metadata;
//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp.Syntax;

//namespace DataLinq.SourceGenerators;

//[Generator]
//public class DataLinqProxyGenerator : ISourceGenerator
//{
//    private MetadataFromFileFactory factory;

//    public void Initialize(GeneratorInitializationContext context)
//    {
//        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
//        factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());
//    }

//    public void Execute(GeneratorExecutionContext context)
//    {
//        if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
//            return;

//        context.ReportDiagnostic(Diagnostic.Create(
//        new DiagnosticDescriptor("DL0001", "Info", "Executing source generator", "Info", DiagnosticSeverity.Info, true), Location.None));


//        var metadata = factory.ReadSyntaxTrees(receiver.ModelDeclarations);

//        //if (metadata.HasFailed)
//        //{
//        //    context.ReportDiagnostic(Diagnostic.Create(
//        //        new DiagnosticDescriptor("DL001", "Error", "Unable to parse source files", "DataLinq", DiagnosticSeverity.Error, true), null));
//        //    return;
//        //}

//        foreach (var table in metadata.TableModels)
//        {
//            var source = GenerateProxyClass(context, table);

//            context.AddSource($"{table.Model.CsTypeName}Proxy.cs", source);
//        }
//    }

//    private string GenerateProxyClass(GeneratorExecutionContext context, TableModelMetadata tableModel)
//    {
//        var namespaceName = tableModel.Model.Database.CsNamespace;

//        if (string.IsNullOrWhiteSpace(namespaceName))
//        {
//               context.ReportDiagnostic(Diagnostic.Create(
//                new DiagnosticDescriptor("DL002", "Error", "Unable to determine namespace", "DataLinq", DiagnosticSeverity.Error, true), null));

//            return string.Empty;
//        }


//        var className = tableModel.Model.CsTypeName;
//        var proxyClassName = $"{className}Proxy";

//        //var methods = classDeclaration.Members.OfType<MethodDeclarationSyntax>()
//        //    .Select(method => method.Identifier.Text);

//        //var properties = classDeclaration.Members.OfType<PropertyDeclarationSyntax>()
//        //    .Select(property => property.Identifier.Text);

//        var sb = new StringBuilder();
//        var tab = "    ";
//        sb.AppendLine("using System;");
//        sb.AppendLine($"namespace {namespaceName};");
//        sb.AppendLine($"public partial record {className}");
//        sb.AppendLine("{");
//        sb.AppendLine($"{tab}public string Generated() => \"Generator: \" + {className};");
//        //sb.AppendLine("}");


//        //var sb = new StringBuilder();
//        //sb.AppendLine("using System;");
//        //sb.AppendLine($"namespace {namespaceName}");
//        //sb.AppendLine("{");
//        //sb.AppendLine($"    public partial record {proxyClassName} : {className}");
//        //sb.AppendLine("    {");
//        //sb.AppendLine($"        private readonly {className} _instance;");
//        //sb.AppendLine($"        public {proxyClassName}({className} instance)");
//        //sb.AppendLine("        {");
//        //sb.AppendLine("            _instance = instance;");
//        //sb.AppendLine("        }");

//        //foreach (var method in methods)
//        //{
//        //    sb.AppendLine($"        public override void {method}()");
//        //    sb.AppendLine("        {");
//        //    sb.AppendLine("            // Intercept method call");
//        //    sb.AppendLine($"            _instance.{method}();");
//        //    sb.AppendLine("        }");
//        //}

//        //foreach (var property in properties)
//        //{
//        //    sb.AppendLine($"        public override string {property}");
//        //    sb.AppendLine("        {");
//        //    sb.AppendLine("            get { return _instance.{property}; }");
//        //    sb.AppendLine("            set { _instance.{property} = value; }");
//        //    sb.AppendLine("        }");
//        //}

//        //sb.AppendLine("    }");
//        //sb.AppendLine("}");

//        return sb.ToString();
//    }

//    private class SyntaxReceiver : ISyntaxReceiver
//    {
//        public List<TypeDeclarationSyntax> ModelDeclarations { get; } = new List<TypeDeclarationSyntax>();

//        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
//        {
//            if (syntaxNode is TypeDeclarationSyntax classDeclaration
//                && classDeclaration.BaseList?.Types.Any(t => MetadataFromFileFactory.IsModelInterface(t.ToString())) == true)
//            {
//                ModelDeclarations.Add(classDeclaration);
//            }
//        }
//    }
//}

