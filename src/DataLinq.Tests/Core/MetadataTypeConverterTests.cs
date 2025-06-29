using System;
using System.Collections.Generic;
using DataLinq.Core.Factories; // Namespace where MetadataTypeConverter resides
using Xunit;

namespace DataLinq.Tests.Core
{
    /*
        [Theory] and [InlineData]: We use xUnit's theory feature to test each method with multiple inputs and expected outputs concisely.

        Coverage: The tests cover various C# built-in types (value types, reference types, keywords vs. full names), custom types (represented by strings like "MyCustomClass"), generic types, and edge cases (like single 'I' for RemoveInterfacePrefix).

        CsTypeSize: Note the assumptions made for DateTime and DateOnly, as their exact managed size isn't easily determined by sizeof. We test the expected convention used in DataLinq (likely based on long Ticks).

        GetKeywordName: Tests conversion both from System.Type and from a string representation.

        Clarity: Each test focuses on a single method of the MetadataTypeConverter.
     */

    public class MetadataTypeConverterTests
    {
        [Theory]
        // Value Types
        [InlineData("sbyte", sizeof(sbyte))]
        [InlineData("byte", sizeof(byte))]
        [InlineData("short", sizeof(short))]
        [InlineData("ushort", sizeof(ushort))]
        [InlineData("int", sizeof(int))]
        [InlineData("uint", sizeof(uint))]
        [InlineData("long", sizeof(long))]
        [InlineData("ulong", sizeof(ulong))]
        [InlineData("char", sizeof(char))]
        [InlineData("float", sizeof(float))]
        [InlineData("double", sizeof(double))]
        [InlineData("bool", sizeof(bool))]
        [InlineData("decimal", sizeof(decimal))]
        [InlineData("DateTime", 8)] // DateTime size isn't sizeof, often estimated or based on Ticks (long)
        [InlineData("DateOnly", sizeof(long))] // Represents as ticks internally? SizeOf wouldn't work. Assuming based on Ticks storage. Adjust if different.
        [InlineData("Guid", 16)] // Guids are 16 bytes
        [InlineData("enum", sizeof(int))] // Assuming underlying type is int
        // Reference Types (expect null size)
        [InlineData("string", null)]
        [InlineData("String", null)]
        [InlineData("byte[]", null)]
        // Unknown/Custom Type
        [InlineData("MyCustomClass", null)]
        public void CsTypeSize_ReturnsCorrectSize(string csTypeName, int? expectedSize)
        {
            // Act
            var actualSize = MetadataTypeConverter.CsTypeSize(csTypeName);

            // Assert
            Assert.Equal(expectedSize, actualSize);
        }

        [Theory]
        [InlineData(typeof(Int32), "int")]
        [InlineData(typeof(String), "string")]
        [InlineData(typeof(Boolean), "bool")]
        [InlineData(typeof(DateTime), "DateTime")] // Not a keyword
        [InlineData(typeof(Guid), "Guid")]
        [InlineData(typeof(Byte[]), "byte[]")]
        [InlineData(typeof(List<Int32>), "List<int>")]
        [InlineData(typeof(Dictionary<String, Int32>), "Dictionary<string, int>")]
        public void GetKeywordName_FromType_ReturnsCorrectName(Type type, string expectedName)
        {
            // Act
            var actualName = MetadataTypeConverter.GetKeywordName(type);

            // Assert
            Assert.Equal(expectedName, actualName);
        }

        [Theory]
        [InlineData("Int32", "int")]
        [InlineData("String", "string")]
        [InlineData("Boolean", "bool")]
        [InlineData("DateTime", "DateTime")] // Not a keyword
        [InlineData("Byte[]", "byte[]")]   // Not a keyword
        [InlineData("MyClass", "MyClass")] // Custom type
        public void GetKeywordName_FromString_ReturnsCorrectName(string typeName, string expectedKeyword)
        {
            // Act
            var actualKeyword = MetadataTypeConverter.GetKeywordName(typeName);

            // Assert
            Assert.Equal(expectedKeyword, actualKeyword);
        }

        [Theory]
        [InlineData("int", "System.Int32")]
        [InlineData("string", "System.String")]
        [InlineData("bool", "System.Boolean")]
        [InlineData("DateTime", "System.DateTime")]
        [InlineData("DateOnly", "System.DateOnly")]
        [InlineData("byte[]", "System.Byte[]")]
        [InlineData("MyNamespace.MyClass", "MyNamespace.MyClass")] // Non-system type
        public void GetFullTypeName_ReturnsCorrectFullName(string typeName, string expectedFullName)
        {
            // Act
            var actualFullName = MetadataTypeConverter.GetFullTypeName(typeName);

            // Assert
            Assert.Equal(expectedFullName, actualFullName);
        }

        [Theory]
        [InlineData("int", true)]
        [InlineData("string", false)] // Reference type
        [InlineData("bool", true)]
        [InlineData("double", true)]
        [InlineData("DateTime", true)]
        [InlineData("DateOnly", true)]
        [InlineData("TimeOnly", true)]
        [InlineData("Guid", true)]
        [InlineData("byte[]", false)] // Reference type
        [InlineData("decimal", true)]
        [InlineData("enum", true)] // Enums are value types
        [InlineData("MyStruct", false)] // Assuming custom structs aren't implicitly nullable like built-ins
        [InlineData("MyClass", false)] // Reference type
        public void IsCsTypeNullable_ReturnsCorrectly(string csTypeName, bool expectedResult)
        {
            // Act
            var actualResult = MetadataTypeConverter.IsCsTypeNullable(csTypeName);

            // Assert
            Assert.Equal(expectedResult, actualResult);
        }

        [Theory]
        [InlineData("int", true)]
        [InlineData("string", true)]
        [InlineData("String", true)]
        [InlineData("bool", true)]
        [InlineData("DateTime", true)]
        [InlineData("Guid", true)]
        [InlineData("byte[]", true)]
        [InlineData("enum", true)]
        [InlineData("MyCustomType", false)]
        [InlineData("System.Int32", false)] // Expecting keyword or simple name
        public void IsKnownCsType_ReturnsCorrectly(string csTypeName, bool expectedResult)
        {
            // Act
            var actualResult = MetadataTypeConverter.IsKnownCsType(csTypeName);

            // Assert
            Assert.Equal(expectedResult, actualResult);
        }

        [Theory]
        [InlineData("bool", true)]
        [InlineData("byte", true)]
        [InlineData("int", true)]
        [InlineData("decimal", true)]
        [InlineData("string", false)] // string is not a primitive type
        [InlineData("String", false)]
        [InlineData("DateTime", false)]
        [InlineData("Guid", false)]
        [InlineData("MyStruct", false)]
        public void IsPrimitiveType_ReturnsCorrectly(string typeName, bool expectedResult)
        {
            // Act
            var actualResult = MetadataTypeConverter.IsPrimitiveType(typeName);

            // Assert
            Assert.Equal(expectedResult, actualResult);
        }

        [Theory]
        [InlineData("IMyInterface", "MyInterface")]
        [InlineData("MyInterface", "MyInterface")] // No leading 'I'
        [InlineData("ICustomType", "CustomType")]
        [InlineData("IDisposable", "Disposable")]
        [InlineData("I", "I")]             // Single 'I' shouldn't be stripped
        [InlineData("iInterface", "iInterface")] // Lowercase 'i' shouldn't be stripped
        [InlineData("MyIClass", "MyIClass")]     // 'I' not at the start
        [InlineData("", "")]
        [InlineData("Customer", "Customer")]
        public void RemoveInterfacePrefix_RemovesPrefixCorrectly(string inputName, string expectedName)
        {
            // Act
            var actualName = MetadataTypeConverter.RemoveInterfacePrefix(inputName);

            // Assert
            Assert.Equal(expectedName, actualName);
        }
    }
}