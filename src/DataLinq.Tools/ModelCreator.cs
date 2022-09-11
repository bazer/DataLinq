using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools.Config;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace DataLinq.Tools
{
    public class ModelCreator
    {
        public Action<string> Log { get; }

        public ModelCreator(Action<string> log)
        {
            Log = log;
        }


        public void Create(ConfigFile config)
        {
            foreach (var database in config.Databases)
            {
                Create(database);

            }
        }

        public void Create(DatabaseConfig db)
        {
            Log($"Reading database: {db.Name}");
            Log($"Type: {db.Type}");

            var dbMetadata = db.ParsedType switch
            {
                DatabaseType.MariaDB or DatabaseType.MySQL =>
                    MySql.MetadataFromSqlFactory.ParseDatabase(db.Name, new MySqlDatabase<information_schema>(db.ConnectionString).Query()),
                DatabaseType.SQLite =>
                    SQLite.MetadataFromSqlFactory.ParseDatabase(db.Name, db.ConnectionString)
            };

            Log($"Tables in database: {dbMetadata.Tables.Count}");
            Log($"Reading models from: {db.SourceDirectory}");

            var srcMetadata = MetadataFromFileFactory.ReadFiles(db.SourceDirectory);

            Log($"Tables in custom files: {srcMetadata.Tables.Count}");

            Log($"Writing models to: {db.DestinationDirectory}");

            var settings = new FileFactorySettings
            {
                NamespaceName = db.Namespace ?? "DataLinq.Models",
                UseRecords = db.UseRecord ?? true,
                UseCache = db.UseCache ?? true
            };

            foreach (var file in FileFactory.CreateModelFiles(dbMetadata, settings))
            {
                var filepath = $"{db.DestinationDirectory}{Path.DirectorySeparatorChar}{file.path}";
                Log($"Writing {filepath}");

                if (!File.Exists(filepath))
                    Directory.CreateDirectory(Path.GetDirectoryName(filepath));

                File.WriteAllText(filepath, file.contents, Encoding.UTF8);
            }
        }

        //public void ReadFiles(string path)
        //{
        //    DirectoryInfo d = new DirectoryInfo(path);
        //    string[] sourceFiles = d.EnumerateFiles("*.cs", SearchOption.AllDirectories)
        //        .Select(a => a.FullName).ToArray();

        //    // 2
        //    List<SyntaxTree> trees = new List<SyntaxTree>();
        //    foreach (string file in sourceFiles)
        //    {
        //        string code = File.ReadAllText(file);
        //        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        //        trees.Add(tree);
        //    }

        //    MetadataReference mscorlib =
        //        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        //    MetadataReference codeAnalysis =
        //            MetadataReference.CreateFromFile(typeof(SyntaxTree).Assembly.Location);
        //    MetadataReference csharpCodeAnalysis =
        //            MetadataReference.CreateFromFile(typeof(CSharpSyntaxTree).Assembly.Location);

        //    MetadataReference[] references = { mscorlib, codeAnalysis, csharpCodeAnalysis };

        //    var compilation = CSharpCompilation.Create("qwerty.dll",
        //       trees,
        //       references,
        //       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        //    //var result = compilation.Emit(Path.Combine(destinationLocation, "qwerty.dll"));

        //    //SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(@"
        //    //    using System;
        //    //    namespace RoslynCompileSample
        //    //    {
        //    //        public class Writer
        //    //        {
        //    //            public void Write(string message)
        //    //            {
        //    //                Console.WriteLine(message);
        //    //            }
        //    //        }
        //    //    }");

        //    //string assemblyName = Path.GetRandomFileName();
        //    //MetadataReference[] references = new MetadataReference[]
        //    //{
        //    //    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        //    //    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        //    //};

        //    //CSharpCompilation compilation = CSharpCompilation.Create(
        //    //    assemblyName,
        //    //    syntaxTrees: new[] { syntaxTree },
        //    //    references: references,
        //    //    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        //    using (var ms = new MemoryStream())
        //    {
        //        EmitResult result = compilation.Emit(ms);

        //        if (!result.Success)
        //        {
        //            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
        //                diagnostic.IsWarningAsError ||
        //                diagnostic.Severity == DiagnosticSeverity.Error);

        //            foreach (Diagnostic diagnostic in failures)
        //            {
        //                Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
        //            }
        //        }
        //        else
        //        {
        //            ms.Seek(0, SeekOrigin.Begin);
        //            Assembly assembly = Assembly.Load(ms.ToArray());

        //            Type type = assembly.GetType("RoslynCompileSample.Writer");
        //            object obj = Activator.CreateInstance(type);
        //            type.InvokeMember("Write",
        //                BindingFlags.Default | BindingFlags.InvokeMethod,
        //                null,
        //                obj,
        //                new object[] { "Hello World" });
        //        }
        //    }

        //    Console.ReadLine();
        //}
    }
}