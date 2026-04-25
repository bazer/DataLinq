using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Generators.Tests;

public class ModelGeneratorInputTests
{
    [Test]
    public async Task ModelDeclarationSnapshot_IgnoresTriviaAndSourceLocation()
    {
        var first = GetTypeDeclaration("""
            namespace SnapshotTests;

            public abstract partial class SnapshotRow : ITableModel<SnapshotDb>
            {
                [Column("id")]
                public abstract int Id { get; }
            }
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\FirstSnapshotRow.cs");

        var second = GetTypeDeclaration("""
            namespace SnapshotTests;

            // Formatting and comments should not affect the structural snapshot.
            public   abstract   partial   class   SnapshotRow
                : ITableModel<SnapshotDb>
            {
                [Column("id")] public abstract int Id { get; }
            }
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\SecondSnapshotRow.cs");

        await Assert.That(ModelDeclarationSnapshot.Create(first))
            .IsEqualTo(ModelDeclarationSnapshot.Create(second));
    }

    [Test]
    public async Task ModelDeclarationInputComparer_UsesStructuralSnapshot()
    {
        var first = ModelDeclarationInput.Create(GetTypeDeclaration("""
            namespace SnapshotTests;

            public abstract partial class SnapshotRow : ITableModel<SnapshotDb>
            {
                [Column("id")]
                public abstract int Id { get; }
            }
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\FirstSnapshotRow.cs"));

        var second = ModelDeclarationInput.Create(GetTypeDeclaration("""
            namespace SnapshotTests;

            public abstract partial class SnapshotRow
                : ITableModel<SnapshotDb>
            {
                [Column("id")] public abstract int Id { get; }
            }
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\SecondSnapshotRow.cs"));

        await Assert.That(ModelDeclarationInputComparer.Instance.Equals(first, second)).IsTrue();
        await Assert.That(ModelDeclarationInputComparer.Instance.GetHashCode(first))
            .IsEqualTo(ModelDeclarationInputComparer.Instance.GetHashCode(second));
    }

    [Test]
    public async Task ModelDeclarationInputArrayComparer_UsesOrderedStructuralSnapshots()
    {
        var first = ModelDeclarationInput.Create(GetTypeDeclaration("""
            namespace SnapshotTests;
            public partial class SnapshotDb : IDatabaseModel {}
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\SnapshotDb1.cs"));

        var second = ModelDeclarationInput.Create(GetTypeDeclaration("""
            namespace SnapshotTests;
            public partial class SnapshotDb : IDatabaseModel { }
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\SnapshotDb2.cs"));

        await Assert.That(ModelDeclarationInputArrayComparer.Instance.Equals([first], [second])).IsTrue();
    }

    [Test]
    public async Task NormalizeModelDeclarationOrder_OrdersByNamespaceAndName()
    {
        var alpha = ModelDeclarationInput.Create(GetTypeDeclaration("""
            namespace AlphaModels;
            public partial class AlphaDb : IDatabaseModel {}
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\B.cs"));

        var beta = ModelDeclarationInput.Create(GetTypeDeclaration("""
            namespace BetaModels;
            public partial class BetaDb : IDatabaseModel {}
            """, @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\A.cs"));

        var normalized = ModelGeneratorInput.NormalizeModelDeclarationOrder([beta, alpha]);

        await Assert.That(string.Join(",", normalized.Select(x => x.Snapshot.Name)))
            .IsEqualTo("AlphaDb,BetaDb");
        await Assert.That(normalized[0].Snapshot.Namespace).IsEqualTo("AlphaModels");
        await Assert.That(normalized[1].Snapshot.Namespace).IsEqualTo("BetaModels");
    }

    private static TypeDeclarationSyntax GetTypeDeclaration(string source, string path)
        => CSharpSyntaxTree.ParseText(source, path: path)
            .GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Single();
}
