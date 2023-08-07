using DataLinq.Extensions;
using DataLinq.Interfaces;
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
using ThrowAway;

namespace DataLinq.Metadata
{
    public enum MetadataFromFileFactoryError
    {
        CompilationError,
        TypeNotFound
    }

    public class MetadataFromFileFactoryOptions
    {
        public Encoding FileEncoding { get; set; } = new UTF8Encoding(false);
        public bool RemoveInterfacePrefix { get; set; } = false;
    }

    internal class MetadataFromFileFactory
    {
        private readonly MetadataFromFileFactoryOptions options;
        public Action<string> Log { get; }

        public MetadataFromFileFactory(MetadataFromFileFactoryOptions options, Action<string> log)
        {
            this.options = options;
            Log = log;
        }

        private static IEnumerable<MetadataReference> GetReferences(params Type[] types)
        {
            foreach (var type in types)
                yield return MetadataReference.CreateFromFile(type.Assembly.Location);
        }

        public Option<DatabaseMetadata, MetadataFromFileFactoryError> ReadFiles(string csType, params string[] paths)
        {
            var trees = new List<SyntaxTree>();

            foreach (var path in paths)
            {
                var sourceFiles = Directory.Exists(path)
                    ? new DirectoryInfo(path)
                        .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                        .Select(a => a.FullName)
                    : new FileInfo(path).FullName.Yield();

                foreach (string file in sourceFiles)
                    trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file, options.FileEncoding)));
            }

            var references = GetReferences(
                typeof(object),
                typeof(System.Runtime.DependentHandle),
                typeof(System.Collections.ObjectModel.ObservableCollection<object>),
                typeof(System.ComponentModel.DesignerCategoryAttribute),
                typeof(System.ComponentModel.DataAnnotations.AssociatedMetadataTypeTypeDescriptionProvider),
                typeof(System.Xml.Serialization.CodeGenerationOptions),
                typeof(Newtonsoft.Json.ConstructorHandling),
                typeof(Remotion.Linq.DefaultQueryProvider),
                typeof(System.Linq.EnumerableExecutor),
                typeof(System.Linq.Expressions.BinaryExpression),
                typeof(DataLinq),
                typeof(SyntaxTree),
                typeof(CSharpSyntaxTree))
                .ToList();

            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location));

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

                    return MetadataFromFileFactoryError.CompilationError;
                }

                ms.Seek(0, SeekOrigin.Begin);
                Assembly assembly = Assembly.Load(ms.ToArray());

                //List<Type> dbTypes = assembly.ExportedTypes.Where(x => x.Name == csType)
                //    .Concat(assembly.ExportedTypes.Where(x => x.Name == "I" + csType))
                //    .ToList();

                

                var types = assembly.ExportedTypes.Where(x => 
                    x.GetInterface("ICustomDatabaseModel") != null ||
                    x.GetInterface("IDatabaseModel") != null ||
                    x.GetInterface("ICustomTableModel") != null ||
                    x.GetInterface("ICustomViewModel") != null)
                    .ToArray();

                if (types.Length == 0)
                {
                    Log($"Couldn't find any type '{csType}' that implements 'IDatabaseModel', 'ICustomDatabaseModel', 'ICustomTableModel' or 'ICustomViewModel'");
                    return MetadataFromFileFactoryError.TypeNotFound;
                }

                return MetadataFromInterfaceFactory.ParseDatabaseFromSources(options.RemoveInterfacePrefix, types);
            }
        }
    }
}
