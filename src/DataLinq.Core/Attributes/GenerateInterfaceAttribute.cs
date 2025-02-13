using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class GenerateInterfaceAttribute : Attribute
{
    public GenerateInterfaceAttribute(bool generateInterface = true)
    {
        GenerateInterface = generateInterface;
    }

    public GenerateInterfaceAttribute(string name)
    {
        Name = name;
        GenerateInterface = true;
    }

    public GenerateInterfaceAttribute(string name, bool generateInterface = true)
    {
        Name = name;
        GenerateInterface = generateInterface;
    }

    public string? Name { get; }
    public bool GenerateInterface { get; }
}