﻿using System;

namespace DataLinq.Extensions.Helpers;

internal static class StringExtensions
{
    //https://stackoverflow.com/a/4405876
    internal static string FirstCharToUpper(this string input) =>
        input switch
        {
            null => throw new ArgumentNullException(nameof(input)),
            "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
            _ => string.Concat(input[0].ToString().ToUpper(), input.Substring(1))
        };
}
