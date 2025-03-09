using System;

namespace DataLinq.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public class InterfaceAttribute : Attribute
{
    public InterfaceAttribute(bool generateInterface = true)
    {
        GenerateInterface = generateInterface;
    }

    public InterfaceAttribute(string name)
    {
        Name = name;
        GenerateInterface = true;
    }

    public InterfaceAttribute(string name, bool generateInterface = true)
    {
        Name = name;
        GenerateInterface = generateInterface;
    }

    public string? Name { get; }
    public bool GenerateInterface { get; }
}

public class InterfaceAttribute<T> : InterfaceAttribute
{
    public InterfaceAttribute(bool generateInterface = true) : base(typeof(T).Name, generateInterface) { }
}