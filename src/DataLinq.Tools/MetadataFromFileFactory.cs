using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DataLinq.Metadata
{
    public class MetadataFromFileFactorySettings
    {

    }

    internal class MetadataFromFileFactory
    {
        public Action<string> Log { get; }

        public MetadataFromFileFactory(Action<string> log)
        {
            Log = log;
        }

        private static IEnumerable<MetadataReference> GetReferences(params Type[] types)
        {
            foreach (var type in types)
                yield return MetadataReference.CreateFromFile(type.Assembly.Location);
        }

        public DatabaseMetadata ReadFiles(string path, string csType)
        {
            var d = new DirectoryInfo(path);
            string[] sourceFiles = d
                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Select(a => a.FullName).ToArray();

            var trees = new List<SyntaxTree>();
            foreach (string file in sourceFiles)
            {
                string code = File.ReadAllText(file);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
                trees.Add(tree);
            }

            var references = GetReferences(
                typeof(object),
                typeof(System.Runtime.DependentHandle),
                typeof(DataLinq),
                typeof(SyntaxTree),
                typeof(CSharpSyntaxTree))
                .ToList();

            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));

            var compilation = CSharpCompilation.Create("datalinq_metadata.dll",
               trees,
               references,
               new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in failures)
                    {
                        Log($"{diagnostic.Id}: {diagnostic.GetMessage()}");
                    }

                    return null;
                }

                ms.Seek(0, SeekOrigin.Begin);
                Assembly assembly = Assembly.Load(ms.ToArray());

                Type type = assembly.ExportedTypes.SingleOrDefault(x => x.Name == csType) ??
                    assembly.ExportedTypes.SingleOrDefault(x => x.Name == "I" + csType);

                if (type == null)
                {
                    Log($"Couldn't find type with name: '{csType}'");
                    return null;
                }

                return MetadataFromInterfaceFactory.ParseDatabase(type);
            }
        }
    }
}
