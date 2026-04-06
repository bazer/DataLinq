using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataLinq.Core.Factories;

namespace DataLinq.Tests.Unit.Core;

public sealed record CsTypeSizeCase(string TypeName, int? ExpectedSize);
public sealed record TypeKeywordCase(Type Type, string ExpectedName);
public sealed record StringKeywordCase(string TypeName, string ExpectedKeyword);
public sealed record FullTypeNameCase(string TypeName, string ExpectedFullName);
public sealed record TypeFlagCase(string TypeName, bool Expected);

public class MetadataTypeConverterTests
{
    [Test]
    [MethodDataSource(nameof(CsTypeSizeCases))]
    public async Task CsTypeSize_ReturnsCorrectSize(CsTypeSizeCase testCase)
    {
        await Assert.That(MetadataTypeConverter.CsTypeSize(testCase.TypeName)).IsEqualTo(testCase.ExpectedSize);
    }

    [Test]
    [MethodDataSource(nameof(TypeKeywordCases))]
    public async Task GetKeywordName_FromType_ReturnsCorrectName(TypeKeywordCase testCase)
    {
        await Assert.That(MetadataTypeConverter.GetKeywordName(testCase.Type)).IsEqualTo(testCase.ExpectedName);
    }

    [Test]
    [MethodDataSource(nameof(StringKeywordCases))]
    public async Task GetKeywordName_FromString_ReturnsCorrectName(StringKeywordCase testCase)
    {
        await Assert.That(MetadataTypeConverter.GetKeywordName(testCase.TypeName)).IsEqualTo(testCase.ExpectedKeyword);
    }

    [Test]
    [MethodDataSource(nameof(FullTypeNameCases))]
    public async Task GetFullTypeName_ReturnsCorrectFullName(FullTypeNameCase testCase)
    {
        await Assert.That(MetadataTypeConverter.GetFullTypeName(testCase.TypeName)).IsEqualTo(testCase.ExpectedFullName);
    }

    [Test]
    [MethodDataSource(nameof(NullableTypeCases))]
    public async Task IsCsTypeNullable_ReturnsCorrectly(TypeFlagCase testCase)
    {
        await Assert.That(MetadataTypeConverter.IsCsTypeNullable(testCase.TypeName)).IsEqualTo(testCase.Expected);
    }

    [Test]
    [MethodDataSource(nameof(KnownTypeCases))]
    public async Task IsKnownCsType_ReturnsCorrectly(TypeFlagCase testCase)
    {
        await Assert.That(MetadataTypeConverter.IsKnownCsType(testCase.TypeName)).IsEqualTo(testCase.Expected);
    }

    [Test]
    [MethodDataSource(nameof(PrimitiveTypeCases))]
    public async Task IsPrimitiveType_ReturnsCorrectly(TypeFlagCase testCase)
    {
        await Assert.That(MetadataTypeConverter.IsPrimitiveType(testCase.TypeName)).IsEqualTo(testCase.Expected);
    }

    [Test]
    [MethodDataSource(nameof(RemoveInterfacePrefixCases))]
    public async Task RemoveInterfacePrefix_RemovesPrefixCorrectly(StringKeywordCase testCase)
    {
        await Assert.That(MetadataTypeConverter.RemoveInterfacePrefix(testCase.TypeName)).IsEqualTo(testCase.ExpectedKeyword);
    }

    public static IEnumerable<Func<CsTypeSizeCase>> CsTypeSizeCases()
    {
        yield return () => new CsTypeSizeCase("sbyte", sizeof(sbyte));
        yield return () => new CsTypeSizeCase("byte", sizeof(byte));
        yield return () => new CsTypeSizeCase("short", sizeof(short));
        yield return () => new CsTypeSizeCase("ushort", sizeof(ushort));
        yield return () => new CsTypeSizeCase("int", sizeof(int));
        yield return () => new CsTypeSizeCase("uint", sizeof(uint));
        yield return () => new CsTypeSizeCase("long", sizeof(long));
        yield return () => new CsTypeSizeCase("ulong", sizeof(ulong));
        yield return () => new CsTypeSizeCase("char", sizeof(char));
        yield return () => new CsTypeSizeCase("float", sizeof(float));
        yield return () => new CsTypeSizeCase("double", sizeof(double));
        yield return () => new CsTypeSizeCase("bool", sizeof(bool));
        yield return () => new CsTypeSizeCase("decimal", sizeof(decimal));
        yield return () => new CsTypeSizeCase("DateTime", 8);
        yield return () => new CsTypeSizeCase("DateOnly", sizeof(long));
        yield return () => new CsTypeSizeCase("Guid", 16);
        yield return () => new CsTypeSizeCase("enum", sizeof(int));
        yield return () => new CsTypeSizeCase("string", null);
        yield return () => new CsTypeSizeCase("String", null);
        yield return () => new CsTypeSizeCase("byte[]", null);
        yield return () => new CsTypeSizeCase("MyCustomClass", null);
    }

    public static IEnumerable<Func<TypeKeywordCase>> TypeKeywordCases()
    {
        yield return () => new TypeKeywordCase(typeof(int), "int");
        yield return () => new TypeKeywordCase(typeof(string), "string");
        yield return () => new TypeKeywordCase(typeof(bool), "bool");
        yield return () => new TypeKeywordCase(typeof(DateTime), "DateTime");
        yield return () => new TypeKeywordCase(typeof(Guid), "Guid");
        yield return () => new TypeKeywordCase(typeof(byte[]), "byte[]");
        yield return () => new TypeKeywordCase(typeof(List<int>), "List<int>");
        yield return () => new TypeKeywordCase(typeof(Dictionary<string, int>), "Dictionary<string, int>");
    }

    public static IEnumerable<Func<StringKeywordCase>> StringKeywordCases()
    {
        yield return () => new StringKeywordCase("Int32", "int");
        yield return () => new StringKeywordCase("String", "string");
        yield return () => new StringKeywordCase("Boolean", "bool");
        yield return () => new StringKeywordCase("DateTime", "DateTime");
        yield return () => new StringKeywordCase("Byte[]", "byte[]");
        yield return () => new StringKeywordCase("MyClass", "MyClass");
    }

    public static IEnumerable<Func<FullTypeNameCase>> FullTypeNameCases()
    {
        yield return () => new FullTypeNameCase("int", "System.Int32");
        yield return () => new FullTypeNameCase("string", "System.String");
        yield return () => new FullTypeNameCase("bool", "System.Boolean");
        yield return () => new FullTypeNameCase("DateTime", "System.DateTime");
        yield return () => new FullTypeNameCase("DateOnly", "System.DateOnly");
        yield return () => new FullTypeNameCase("byte[]", "System.Byte[]");
        yield return () => new FullTypeNameCase("MyNamespace.MyClass", "MyNamespace.MyClass");
    }

    public static IEnumerable<Func<TypeFlagCase>> NullableTypeCases()
    {
        yield return () => new TypeFlagCase("int", true);
        yield return () => new TypeFlagCase("string", false);
        yield return () => new TypeFlagCase("bool", true);
        yield return () => new TypeFlagCase("double", true);
        yield return () => new TypeFlagCase("DateTime", true);
        yield return () => new TypeFlagCase("DateOnly", true);
        yield return () => new TypeFlagCase("TimeOnly", true);
        yield return () => new TypeFlagCase("Guid", true);
        yield return () => new TypeFlagCase("byte[]", false);
        yield return () => new TypeFlagCase("decimal", true);
        yield return () => new TypeFlagCase("enum", true);
        yield return () => new TypeFlagCase("MyStruct", false);
        yield return () => new TypeFlagCase("MyClass", false);
    }

    public static IEnumerable<Func<TypeFlagCase>> KnownTypeCases()
    {
        yield return () => new TypeFlagCase("int", true);
        yield return () => new TypeFlagCase("string", true);
        yield return () => new TypeFlagCase("String", true);
        yield return () => new TypeFlagCase("bool", true);
        yield return () => new TypeFlagCase("DateTime", true);
        yield return () => new TypeFlagCase("Guid", true);
        yield return () => new TypeFlagCase("byte[]", true);
        yield return () => new TypeFlagCase("enum", true);
        yield return () => new TypeFlagCase("MyCustomType", false);
        yield return () => new TypeFlagCase("System.Int32", false);
    }

    public static IEnumerable<Func<TypeFlagCase>> PrimitiveTypeCases()
    {
        yield return () => new TypeFlagCase("bool", true);
        yield return () => new TypeFlagCase("byte", true);
        yield return () => new TypeFlagCase("int", true);
        yield return () => new TypeFlagCase("decimal", true);
        yield return () => new TypeFlagCase("string", false);
        yield return () => new TypeFlagCase("String", false);
        yield return () => new TypeFlagCase("DateTime", false);
        yield return () => new TypeFlagCase("Guid", false);
        yield return () => new TypeFlagCase("MyStruct", false);
    }

    public static IEnumerable<Func<StringKeywordCase>> RemoveInterfacePrefixCases()
    {
        yield return () => new StringKeywordCase("IMyInterface", "MyInterface");
        yield return () => new StringKeywordCase("MyInterface", "MyInterface");
        yield return () => new StringKeywordCase("ICustomType", "CustomType");
        yield return () => new StringKeywordCase("IDisposable", "Disposable");
        yield return () => new StringKeywordCase("I", "I");
        yield return () => new StringKeywordCase("iInterface", "iInterface");
        yield return () => new StringKeywordCase("MyIClass", "MyIClass");
        yield return () => new StringKeywordCase(string.Empty, string.Empty);
        yield return () => new StringKeywordCase("Customer", "Customer");
    }
}
