using System;
using System.Collections.Generic;

namespace DataLinq.Core.Factories;

internal static class ModelContractName
{
    public static bool IsTableModelContract(string interfaceName) =>
        MatchesModelInterfaceContract(interfaceName, "ITableModel", allowSingleGenericArgument: true);

    public static bool IsViewModelContract(string interfaceName) =>
        MatchesModelInterfaceContract(interfaceName, "IViewModel", allowSingleGenericArgument: true);

    public static bool TryGetInvalidModelInterfaceContractArity(
        string interfaceName,
        out string contractName,
        out int typeArgumentCount,
        out string expectedDescription)
    {
        contractName = string.Empty;
        typeArgumentCount = 0;
        expectedDescription = string.Empty;

        var unqualifiedTypeName = GetUnqualifiedTypeName(interfaceName);
        if (!TrySplitGenericTypeName(unqualifiedTypeName, out var genericName, out var typeArguments))
            return false;

        if (string.Equals(genericName, "IDatabaseModel", StringComparison.Ordinal))
        {
            contractName = genericName;
            typeArgumentCount = typeArguments.Count;
            expectedDescription = "must be non-generic";
            return true;
        }

        if ((string.Equals(genericName, "ITableModel", StringComparison.Ordinal) ||
            string.Equals(genericName, "IViewModel", StringComparison.Ordinal) ||
            string.Equals(genericName, "IModelInstance", StringComparison.Ordinal)) &&
            typeArguments.Count != 1)
        {
            contractName = genericName;
            typeArgumentCount = typeArguments.Count;
            expectedDescription = "must be non-generic or use exactly one database type argument";
            return true;
        }

        return false;
    }

    private static bool MatchesModelInterfaceContract(
        string interfaceName,
        string expectedTypeName,
        bool allowSingleGenericArgument)
    {
        var unqualifiedTypeName = GetUnqualifiedTypeName(interfaceName);
        if (string.Equals(unqualifiedTypeName, expectedTypeName, StringComparison.Ordinal))
            return true;

        return allowSingleGenericArgument &&
            TrySplitGenericTypeName(unqualifiedTypeName, out var genericName, out var typeArguments) &&
            string.Equals(genericName, expectedTypeName, StringComparison.Ordinal) &&
            typeArguments.Count == 1;
    }

    private static bool TrySplitGenericTypeName(
        string typeName,
        out string genericName,
        out IReadOnlyList<string> typeArguments)
    {
        genericName = string.Empty;
        typeArguments = [];

        var genericStart = typeName.IndexOf('<');
        if (genericStart < 0 || !typeName.EndsWith(">", StringComparison.Ordinal))
            return false;

        genericName = typeName.Substring(0, genericStart).Trim();
        var argumentsText = typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2);
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            typeArguments = arguments;
            return true;
        }

        var depth = 0;
        var argumentStart = 0;
        for (var i = 0; i < argumentsText.Length; i++)
        {
            if (argumentsText[i] == '<')
                depth++;
            else if (argumentsText[i] == '>')
                depth--;
            else if (argumentsText[i] == ',' && depth == 0)
            {
                arguments.Add(argumentsText.Substring(argumentStart, i - argumentStart).Trim());
                argumentStart = i + 1;
            }

            if (depth < 0)
                return false;
        }

        if (depth != 0)
            return false;

        arguments.Add(argumentsText.Substring(argumentStart).Trim());
        typeArguments = arguments;
        return true;
    }

    private static string GetUnqualifiedTypeName(string typeName)
    {
        var trimmedTypeName = typeName.Trim();
        var genericStart = trimmedTypeName.IndexOf('<');
        var prefix = genericStart >= 0
            ? trimmedTypeName.Substring(0, genericStart)
            : trimmedTypeName;
        var suffix = genericStart >= 0
            ? trimmedTypeName.Substring(genericStart)
            : string.Empty;

        var dotIndex = prefix.LastIndexOf('.');
        var aliasIndex = LastAliasSeparatorIndex(prefix);
        var separatorIndex = Math.Max(dotIndex, aliasIndex);

        return separatorIndex >= 0
            ? prefix.Substring(separatorIndex + (prefix[separatorIndex] == ':' ? 2 : 1)) + suffix
            : trimmedTypeName;
    }

    private static int LastAliasSeparatorIndex(string text)
    {
        for (var i = text.Length - 2; i >= 0; i--)
        {
            if (text[i] == ':' && text[i + 1] == ':')
                return i;
        }

        return -1;
    }
}
