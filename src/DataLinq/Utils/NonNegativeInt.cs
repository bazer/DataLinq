using System;

namespace DataLinq.Utils;

public class NonNegativeInt
{
    private int value;

    public NonNegativeInt(int initialValue = 0)
    {
        if (initialValue < 0)
            throw new ArgumentException("Initial value cannot be negative.");

        value = initialValue;
    }

    public int Value => value;

    public int Increment()
    {
        return value++;
    }

    public int Decrement()
    {
        return value > 0
            ? value--
            : value;
    }

    public override string ToString() => value.ToString();
}