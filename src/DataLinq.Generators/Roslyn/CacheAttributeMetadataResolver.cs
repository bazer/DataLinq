using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;

namespace DataLinq.SourceGenerators;

internal static class CacheAttributeMetadataResolver
{
    // Resolve only the cache flag here. This does not merge arbitrary attributes or
    // model members from partial declarations into the syntax parser's metadata.
    public static IDLOptionFailure? Apply(DatabaseDefinition database, Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var cacheType = compilation.GetTypeByMetadataName("DataLinq.Attributes.UseCacheAttribute");
        if (cacheType is null)
            return null;
        var failure = Apply(database.CsType, database.Attributes, database.SetAttributesCore,
            flag => database.SetCacheCore(flag ?? false));
        if (failure is not null)
            return failure;
        foreach (var tableModel in database.TableModels.Where(static table => !table.IsStub))
        {
            failure = Apply(tableModel.Model.CsType, tableModel.Model.Attributes, tableModel.Model.SetAttributesCore,
                flag => tableModel.Table.explicitUseCache = flag);
            if (failure is not null)
                return failure;
        }
        return null;

        IDLOptionFailure? Apply(CsTypeDeclaration type, IEnumerable<Attribute> attributes,
            Action<IEnumerable<Attribute>> setAttributes, Action<bool?> setFlag)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = compilation.GetTypeByMetadataName(string.IsNullOrWhiteSpace(type.Namespace)
                ? type.Name : $"{type.Namespace}.{type.Name}");
            if (symbol is null)
                return null;
            // Roslyn combines attributes across every partial declaration of this type.
            var cacheAttributes = symbol.GetAttributes().Where(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, cacheType)).ToArray();
            if (cacheAttributes.Length > 1)
                return Failure(cacheAttributes[1], $"Model '{type.Name}' has multiple [UseCache] attributes.");
            UseCacheAttribute? resolved = null;
            if (cacheAttributes.Length == 1)
            {
                var attribute = cacheAttributes[0];
                if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not bool flag)
                    return Failure(attribute, $"[UseCache] on '{type.Name}' requires a constant boolean argument.");
                resolved = new UseCacheAttribute(flag);
            }
            setAttributes(attributes.Where(static attribute => attribute is not UseCacheAttribute)
                .Concat(resolved is null ? Array.Empty<Attribute>() : new Attribute[] { resolved }));
            setFlag(resolved?.UseCache);
            return null;
        }

        IDLOptionFailure Failure(AttributeData attribute, string message)
        {
            var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken);
            return syntax is null || string.IsNullOrWhiteSpace(syntax.SyntaxTree.FilePath)
                ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message)
                : DLOptionFailure.Fail(DLFailureType.InvalidModel, message,
                    new SourceLocation(new CsFileDeclaration(syntax.SyntaxTree.FilePath), new SourceTextSpan(syntax.SpanStart, syntax.Span.Length)));
        }
    }
}
