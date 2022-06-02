using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace DataLinq.Tools
{
    public class CreateModels
    {
        public void Execute(string dbname, string namespaceName, string path, MySqlDatabase<information_schema> information_schema)
        {
            var database = MetadataFromSqlFactory.ParseDatabase(dbname, information_schema.Query());

            Console.WriteLine($"Database: {dbname}");
            Console.WriteLine($"Table count: {database.Tables.Count}");
            Console.WriteLine($"Writing models to: {path}");

            var settings = new FileFactorySettings
            {
                NamespaceName = namespaceName,
                UseRecords = true,
                UseCache = true
            };

            foreach (var file in FileFactory.CreateModelFiles(database, settings))
            {
                var filepath = $"{path}{Path.DirectorySeparatorChar}{file.path}";
                Console.WriteLine($"Writing {filepath}");

                if (!File.Exists(filepath))
                    Directory.CreateDirectory(Path.GetDirectoryName(filepath));

                File.WriteAllText(filepath, file.contents, Encoding.GetEncoding("iso-8859-1"));
            }
        }

        public void ReadFiles(string path)
        {
            DirectoryInfo d = new DirectoryInfo(path);
            string[] sourceFiles = d.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Select(a => a.FullName).ToArray();

            // 2
            List<SyntaxTree> trees = new List<SyntaxTree>();
            foreach (string file in sourceFiles)
            {
                string code = File.ReadAllText(file);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
                trees.Add(tree);
            }

            MetadataReference mscorlib =
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            MetadataReference codeAnalysis =
                    MetadataReference.CreateFromFile(typeof(SyntaxTree).Assembly.Location);
            MetadataReference csharpCodeAnalysis =
                    MetadataReference.CreateFromFile(typeof(CSharpSyntaxTree).Assembly.Location);

            MetadataReference[] references = { mscorlib, codeAnalysis, csharpCodeAnalysis };

            var compilation = CSharpCompilation.Create("qwerty.dll",
               trees,
               references,
               new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            //var result = compilation.Emit(Path.Combine(destinationLocation, "qwerty.dll"));

            //SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(@"
            //    using System;
            //    namespace RoslynCompileSample
            //    {
            //        public class Writer
            //        {
            //            public void Write(string message)
            //            {
            //                Console.WriteLine(message);
            //            }
            //        }
            //    }");

            //string assemblyName = Path.GetRandomFileName();
            //MetadataReference[] references = new MetadataReference[]
            //{
            //    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            //    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            //};

            //CSharpCompilation compilation = CSharpCompilation.Create(
            //    assemblyName,
            //    syntaxTrees: new[] { syntaxTree },
            //    references: references,
            //    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);

                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in failures)
                    {
                        Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                    }
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    Assembly assembly = Assembly.Load(ms.ToArray());

                    Type type = assembly.GetType("RoslynCompileSample.Writer");
                    object obj = Activator.CreateInstance(type);
                    type.InvokeMember("Write",
                        BindingFlags.Default | BindingFlags.InvokeMethod,
                        null,
                        obj,
                        new object[] { "Hello World" });
                }
            }

            Console.ReadLine();
        }
    }
}