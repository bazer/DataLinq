using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public class SyntaxParser
{
    private static readonly string[] modelInterfaceNames = ["IDatabaseModel", "ITableModel", "IViewModel", "IModelInstance"];
    private readonly ImmutableArray<TypeDeclarationSyntax> modelSyntaxes;
    private readonly ImmutableArray<EnumDeclarationSyntax> enumSyntaxes;

    public static bool IsModelInterface(string interfaceName) =>
        modelInterfaceNames.Any(interfaceName.StartsWith);
    
    public SyntaxParser(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes, ImmutableArray<EnumDeclarationSyntax> enumSyntaxes = default)
    {
        this.modelSyntaxes = modelSyntaxes;
        this.enumSyntaxes = enumSyntaxes.IsDefault ? [] : enumSyntaxes;
    }

    public Option<TableModel, IDLOptionFailure> ParseTableModel(DatabaseDefinition database, TypeDeclarationSyntax typeSyntax, string csPropertyName)
    {
        ModelDefinition model;
        if (typeSyntax == null)
        {
            model = new ModelDefinition(new CsTypeDeclaration(csPropertyName, database.CsType.Namespace, ModelCsType.Interface));
            model.SetInterfacesCore([new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);
        }
        else
        {
            if (!ParseModel(typeSyntax).TryUnwrap(out model, out var failure))
                return failure;
        }

        return new TableModel(csPropertyName, database, model, typeSyntax == null);
    }

    public Option<MetadataTableModelDraft, IDLOptionFailure> ParseTableModelDraft(
        CsTypeDeclaration databaseCsType,
        TypeDeclarationSyntax? typeSyntax,
        string csPropertyName)
    {
        MetadataModelDraft model;
        if (typeSyntax == null)
        {
            model = new MetadataModelDraft(new CsTypeDeclaration(csPropertyName, databaseCsType.Namespace, ModelCsType.Interface))
            {
                OriginalInterfaces = [new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]
            };
        }
        else
        {
            if (!ParseModelDraft(typeSyntax).TryUnwrap(out model, out var failure))
                return failure;
        }

        if (!ParseTableDraft(model).TryUnwrap(out var table, out var tableFailure))
            return tableFailure;

        return new MetadataTableModelDraft(csPropertyName, model, table)
        {
            IsStub = typeSyntax == null
        };
    }

    private Option<ModelDefinition, IDLOptionFailure> ParseModel(TypeDeclarationSyntax typeSyntax)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(typeSyntax));
        model.SetSourceSpanCore(new SourceTextSpan(typeSyntax.SpanStart, typeSyntax.Span.Length));

        if (!string.IsNullOrEmpty(typeSyntax.SyntaxTree.FilePath))
            model.SetCsFileCore(new CsFileDeclaration(typeSyntax.SyntaxTree.FilePath));

        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes", model, failures);

        model.SetAttributesCore(parsedAttributes);
        foreach (var (attribute, sourceSpan) in attributeSourceSpans)
            model.SetAttributeSourceSpanCore(attribute, sourceSpan);

        var modelInstanceInterfaces = model.Attributes
            .Where(x => x is InterfaceAttribute interfaceAttribute && interfaceAttribute.GenerateInterface)
            .Select(x => x as InterfaceAttribute)
            .Select(x => new CsTypeDeclaration(x?.Name ?? $"I{model.CsType.Name}", model.CsType.Namespace, ModelCsType.Interface))
            .ToList();

        if (modelInstanceInterfaces.Count > 1)
            return DLOptionFailure.Fail(DLFailureType.InvalidArgument,
                $"Multiple model instance interfaces ({modelInstanceInterfaces.Select(x => x.Name).ToJoinedString(", ")}) found in model", model);

        //if (modelInstanceInterfaces.GroupBy(x => x.Name).Any(x => x.Count() > 1))
        //    return DLOptionFailure.Fail(DLFailureType.InvalidArgument,
        //        $"Duplicate interface names {modelInstanceInterfaces.GroupBy(x => x.Name).Where(x => x.Count() > 1).ToJoinedString()} in model '{model.CsType.Name}'");

        if (modelInstanceInterfaces.Any())
            model.SetModelInstanceInterfaceCore(modelInstanceInterfaces.First());

        if (typeSyntax.BaseList != null)
        {
            // Build all interfaces from the BaseList.
            var interfaces = typeSyntax.BaseList.Types
                .Select(ParseDeclaredInterface)
                .Where(x => !x.Name.StartsWith("Immutable<"))
                .ToList();

            model.SetInterfacesCore(interfaces);
        }

        if (model.CsType.ModelCsType == ModelCsType.Interface)
            model.SetCsTypeCore(model.CsType.MutateName(MetadataTypeConverter.RemoveInterfacePrefix(model.CsType.Name)));

        if (!typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(attr => attr.Name.ToString() == "Column" || attr.Name.ToString() == "Relation"))
            .Select(prop => ParseProperty(prop, model))
            .Transpose()
            .TryUnwrap(out var properties, out var propFailures))
            return DLOptionFailure.Fail($"Parsing properties", model, propFailures);

        model.AddPropertiesCore(properties);

        model.SetUsingsCore(typeSyntax.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(uds => uds?.Name?.ToString())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .OrderBy(ns => ns!.StartsWith("System"))
            .ThenBy(ns => ns)
            .Select(ns => new ModelUsing(ns!)));

        return model;
    }

    private Option<MetadataModelDraft, IDLOptionFailure> ParseModelDraft(TypeDeclarationSyntax typeSyntax)
    {
        var csType = new CsTypeDeclaration(typeSyntax);
        var csFile = !string.IsNullOrEmpty(typeSyntax.SyntaxTree.FilePath)
            ? new CsFileDeclaration(typeSyntax.SyntaxTree.FilePath)
            : (CsFileDeclaration?)null;
        var sourceSpan = new SourceTextSpan(typeSyntax.SpanStart, typeSyntax.Span.Length);

        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes for {typeSyntax.Identifier.Text}", failures);

        var modelInstanceInterfaces = parsedAttributes
            .Where(x => x is InterfaceAttribute interfaceAttribute && interfaceAttribute.GenerateInterface)
            .Select(x => x as InterfaceAttribute)
            .Select(x => new CsTypeDeclaration(x?.Name ?? $"I{csType.Name}", csType.Namespace, ModelCsType.Interface))
            .ToList();

        if (modelInstanceInterfaces.Count > 1)
            return FailType(
                typeSyntax,
                DLFailureType.InvalidArgument,
                $"Multiple model instance interfaces ({modelInstanceInterfaces.Select(x => x.Name).ToJoinedString(", ")}) found in model");

        var interfaces = typeSyntax.BaseList != null
            ? typeSyntax.BaseList.Types
                .Select(ParseDeclaredInterface)
                .Where(x => !x.Name.StartsWith("Immutable<"))
                .ToArray()
            : [];

        if (csType.ModelCsType == ModelCsType.Interface)
            csType = csType.MutateName(MetadataTypeConverter.RemoveInterfacePrefix(csType.Name));

        if (!typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(attr => attr.Name.ToString() == "Column" || attr.Name.ToString() == "Relation"))
            .Select(ParsePropertyDraft)
            .Transpose()
            .TryUnwrap(out var properties, out var propFailures))
            return DLOptionFailure.Fail($"Parsing properties for {typeSyntax.Identifier.Text}", propFailures);

        var valueProperties = properties
            .OfType<MetadataValuePropertyDraft>()
            .ToArray();
        var relationProperties = properties
            .OfType<MetadataRelationPropertyDraft>()
            .ToArray();
        var usings = typeSyntax.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(uds => uds?.Name?.ToString())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .OrderBy(ns => ns!.StartsWith("System"))
            .ThenBy(ns => ns)
            .Select(ns => new ModelUsing(ns!))
            .ToArray();

        return new MetadataModelDraft(csType)
        {
            CsFile = csFile,
            SourceSpan = sourceSpan,
            Attributes = parsedAttributes,
            AttributeSourceSpans = attributeSourceSpans,
            ModelInstanceInterface = modelInstanceInterfaces.Count == 1
                ? modelInstanceInterfaces[0]
                : null,
            OriginalInterfaces = interfaces,
            Usings = usings,
            ValueProperties = valueProperties,
            RelationProperties = relationProperties
        };
    }

    private static Option<MetadataTableDraft, IDLOptionFailure> ParseTableDraft(MetadataModelDraft model)
    {
        TableType tableType;
        if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("ITableModel")))
            tableType = TableType.Table;
        else if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("IViewModel")))
            tableType = TableType.View;
        else
            return FailModelDraft(model, DLFailureType.InvalidModel, $"Model '{model.CsType.Name}' does not inherit from 'ITableModel' or 'IViewModel'.");

        var dbName = model.CsType.Name;
        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                dbName = tableAttribute.Name;

            if (attribute is ViewAttribute viewAttribute)
                dbName = viewAttribute.Name;
        }

        return new MetadataTableDraft(dbName)
        {
            Type = tableType,
            Definition = model.Attributes
                .OfType<DefinitionAttribute>()
                .LastOrDefault()?
                .Sql,
            UseCache = model.Attributes
                .OfType<UseCacheAttribute>()
                .LastOrDefault()?
                .UseCache,
            CacheLimits = model.Attributes
                .OfType<CacheLimitAttribute>()
                .Select(x => (x.LimitType, x.Amount))
                .ToArray(),
            IndexCache = model.Attributes
                .OfType<IndexCacheAttribute>()
                .Select(x => (x.Type, x.Amount))
                .ToArray()
        };
    }

    private static CsTypeDeclaration ParseDeclaredInterface(BaseTypeSyntax baseType)
    {
        var declaration = new CsTypeDeclaration(baseType);
        return new CsTypeDeclaration(declaration.Name, declaration.Namespace, ModelCsType.Interface);
    }

    public Option<Attribute, IDLOptionFailure> ParseAttribute(AttributeSyntax attributeSyntax)
    {
        var name = attributeSyntax.Name.ToString();
        var generictype = attributeSyntax.Name as GenericNameSyntax;
        var arguments = attributeSyntax.ArgumentList?.Arguments
            .Select(ParseAttributeArgument)
            .ToList() ?? [];

        if (name == "Database")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new DatabaseAttribute(arguments[0]);
        }

        if (name == "Table")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new TableAttribute(arguments[0]);
        }

        if (name == "View")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new ViewAttribute(arguments[0]);
        }

        if (name == "Column")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new ColumnAttribute(arguments[0]);
        }

        if (name == "Comment")
        {
            if (arguments.Count == 1)
                return new CommentAttribute(arguments[0]);

            if (arguments.Count == 2)
            {
                if (!Enum.TryParse(arguments[0].Split('.').Last(), out DatabaseType databaseType))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid DatabaseType value '{arguments[0]}'");

                return new CommentAttribute(databaseType, arguments[1]);
            }

            return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 1 or 2 arguments");
        }

        if (name == "Check")
        {
            if (arguments.Count == 2)
                return new CheckAttribute(arguments[0], arguments[1]);

            if (arguments.Count == 3)
            {
                if (!Enum.TryParse(arguments[0].Split('.').Last(), out DatabaseType databaseType))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid DatabaseType value '{arguments[0]}'");

                return new CheckAttribute(databaseType, arguments[1], arguments[2]);
            }

            return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 or 3 arguments");
        }

        if (name == "Definition")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new DefinitionAttribute(arguments[0]);
        }

        if (name == "UseCache")
        {
            if (arguments.Count == 1)
            {
                if (!TryParseBool(arguments[0], out var useCache))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid boolean value '{arguments[0]}' for attribute '{name}'");

                return new UseCacheAttribute(useCache);
            }
            else
                return new UseCacheAttribute();
        }

        if (name == "CacheLimit")
        {
            if (arguments.Count != 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheLimitType limitType))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid CacheLimitType value '{arguments[0]}'");

            if (!TryParseLong(arguments[1], out var amount))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid cache limit amount '{arguments[1]}'");

            return new CacheLimitAttribute(limitType, amount);
        }

        if (name == "CacheCleanup")
        {
            if (arguments.Count != 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheCleanupType cleanupType))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid CacheCleanupType value '{arguments[0]}'");

            if (!TryParseLong(arguments[1], out var amount))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid cache cleanup amount '{arguments[1]}'");

            return new CacheCleanupAttribute(cleanupType, amount);
        }

        if (name == "IndexCache")
        {
            if (arguments.Count < 1 || arguments.Count > 2)
            {
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 1 or 2 arguments");
            }

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out IndexCacheType indexCacheType))
            {
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid IndexCacheType value '{arguments[0]}'");
            }

            if (arguments.Count == 1)
                return new IndexCacheAttribute(indexCacheType);

            if (!TryParseInt(arguments[1], out var amount))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid index cache amount '{arguments[1]}'");

            return new IndexCacheAttribute(indexCacheType, amount);
        }

        if (name == "AutoIncrement")
        {
            if (arguments.Any())
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new AutoIncrementAttribute();
        }

        if (name == "Relation")
        {
            var rawArguments = attributeSyntax.ArgumentList?.Arguments.ToList() ?? [];
            if (arguments.Count == 2)
                return new RelationAttribute(arguments[0], ParseAttributeStringArrayArgument(rawArguments[1]));
            else if (arguments.Count == 3)
                return new RelationAttribute(arguments[0], ParseAttributeStringArrayArgument(rawArguments[1]), arguments[2]);
            else
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 or 3 arguments");
        }

        if (name == "PrimaryKey")
        {
            if (arguments.Any())
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new PrimaryKeyAttribute();
        }

        if (name == "ForeignKey")
        {
            if (arguments.Count == 3)
                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2]);

            if (arguments.Count == 4)
            {
                if (!TryParseInt(arguments[3], out var ordinal))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ordinal value '{arguments[3]}' for attribute '{name}'");

                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2], ordinal);
            }

            if (arguments.Count == 5)
            {
                if (!TryParseReferentialAction(arguments[3], out var onUpdate))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON UPDATE referential action '{arguments[3]}' for attribute '{name}'");

                if (!TryParseReferentialAction(arguments[4], out var onDelete))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON DELETE referential action '{arguments[4]}' for attribute '{name}'");

                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2], onUpdate, onDelete);
            }

            if (arguments.Count == 6)
            {
                if (!TryParseInt(arguments[3], out var compositeOrdinal))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ordinal value '{arguments[3]}' for attribute '{name}'");

                if (!TryParseReferentialAction(arguments[4], out var onUpdate))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON UPDATE referential action '{arguments[4]}' for attribute '{name}'");

                if (!TryParseReferentialAction(arguments[5], out var onDelete))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON DELETE referential action '{arguments[5]}' for attribute '{name}'");

                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2], compositeOrdinal, onUpdate, onDelete);
            }

            return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' must have 3, 4, 5, or 6 arguments");
        }

        if (name == "Enum")
        {
            return new EnumAttribute([.. arguments]);
        }

        if (name == "Nullable")
        {
            if (arguments.Any())
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new NullableAttribute();
        }

        if (name == "Default")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            var codeExpression = attributeSyntax.ArgumentList?.Arguments[0].Expression.ToString();
            return new DefaultAttribute(arguments[0]).SetCodeExpression(codeExpression);
        }

        if (name == "DefaultSql")
        {
            if (arguments.Count != 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' must have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out DatabaseType databaseType))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid DatabaseType value '{arguments[0]}'");

            return new DefaultSqlAttribute(databaseType, arguments[1]);
        }

        if (name == "DefaultCurrentTimestamp")
        {
            if (arguments.Count != 0)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new DefaultCurrentTimestampAttribute();
        }

        if (name == "DefaultNewUUID")
        {
            if (arguments.Count > 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            if (arguments.Count == 1)
            {
                if (!UUIDVersion.TryParse(arguments[0], out UUIDVersion version))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid UUIDVersion value '{arguments[0]}'");

                return new DefaultNewUUIDAttribute(version);
            }
        
            return new DefaultNewUUIDAttribute();
        }

        if (name == "Index")
        {
            if (arguments.Count < 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            string indexName = arguments[0];
            if (string.IsNullOrWhiteSpace(indexName))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, "Index name cannot be empty.");

            if (!Enum.TryParse(arguments[1].Split('.').Last(), out IndexCharacteristic characteristic))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid IndexCharacteristic value '{arguments[1]}'");

            if (arguments.Count == 2)
                return new IndexAttribute(arguments[0], characteristic);

            if (Enum.TryParse(arguments[2].Split('.').Last(), out IndexType type))
                return new IndexAttribute(indexName, characteristic, type, arguments.Skip(3).ToArray());
            else
                return new IndexAttribute(indexName, characteristic, arguments.Skip(2).ToArray());
        }


        if (name == "Type")
        {
            Option<Attribute, IDLOptionFailure> FailInvalidTypeArgument(string argumentName, string value) =>
                FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid TypeAttribute {argumentName} value '{value}'");

            if (arguments.Count == 0)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            string enumValue = arguments[0].Split('.').Last();
            if (Enum.TryParse(enumValue, out DatabaseType dbType))
            {
                switch (arguments.Count)
                {
                    case 1: return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");
                    case 2: return new TypeAttribute(dbType, arguments[1]);
                    case 3:
                        if (TryParseUlong(arguments[2], out ulong length))
                            return new TypeAttribute(dbType, arguments[1], length);

                        if (TryParseBool(arguments[2], out bool signed))
                            return new TypeAttribute(dbType, arguments[1], signed);

                        return FailInvalidTypeArgument("length or signed", arguments[2]);
                    case 4:
                        if (!TryParseUlong(arguments[2], out length))
                            return FailInvalidTypeArgument("length", arguments[2]);

                        if (TryParseUint(arguments[3], out uint decimals))
                            return new TypeAttribute(dbType, arguments[1], length, decimals);

                        if (TryParseBool(arguments[3], out signed))
                            return new TypeAttribute(dbType, arguments[1], length, signed);

                        return FailInvalidTypeArgument("decimals or signed", arguments[3]);
                    case 5:
                        if (!TryParseUlong(arguments[2], out length))
                            return FailInvalidTypeArgument("length", arguments[2]);

                        if (!TryParseUint(arguments[3], out decimals))
                            return FailInvalidTypeArgument("decimals", arguments[3]);

                        if (!TryParseBool(arguments[4], out signed))
                            return FailInvalidTypeArgument("signed", arguments[4]);

                        return new TypeAttribute(dbType, arguments[1], length, decimals, signed);
                }
            }
            else
            {
                switch (arguments.Count)
                {
                    case 1: return new TypeAttribute(arguments[0]);
                    case 2:
                        if (TryParseUlong(arguments[1], out ulong length))
                            return new TypeAttribute(arguments[0], length);

                        if (TryParseBool(arguments[1], out bool signed))
                            return new TypeAttribute(arguments[0], signed);

                        return FailInvalidTypeArgument("length or signed", arguments[1]);
                    case 3:
                        if (!TryParseUlong(arguments[1], out length))
                            return FailInvalidTypeArgument("length", arguments[1]);

                        if (!TryParseBool(arguments[2], out signed))
                            return FailInvalidTypeArgument("signed", arguments[2]);

                        return new TypeAttribute(arguments[0], length, signed);
                    case 4:
                        if (!TryParseUlong(arguments[1], out length))
                            return FailInvalidTypeArgument("length", arguments[1]);

                        if (!TryParseUint(arguments[2], out uint decimals))
                            return FailInvalidTypeArgument("decimals", arguments[2]);

                        if (!TryParseBool(arguments[3], out signed))
                            return FailInvalidTypeArgument("signed", arguments[3]);

                        return new TypeAttribute(arguments[0], length, decimals, signed);
                }
            }

            return FailAttribute(attributeSyntax, DLFailureType.NotImplemented, $"Attribute 'TypeAttribute' with {arguments.Count} arguments not implemented");
        }

        if (name.StartsWith("Interface"))
        {
            // Handle generic InterfaceAttribute<T>
            if (generictype != null)
            {
                if (arguments.Count > 1)
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

                // Extract the type argument from the generic type
                var typeArgument = generictype.TypeArgumentList.Arguments[0].ToString();
                if (arguments.Count == 0)
                    return new InterfaceAttribute(typeArgument);

                if (!TryParseBool(arguments[0], out bool generateInterface))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid InterfaceAttribute generateInterface value '{arguments[0]}'");

                return new InterfaceAttribute(typeArgument, generateInterface);
            }

            if (arguments.Count == 1)
                return new InterfaceAttribute(arguments[0]);
            else if (arguments.Count == 2)
            {
                if (!TryParseBool(arguments[1], out bool generateInterface))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid InterfaceAttribute generateInterface value '{arguments[1]}'");

                return new InterfaceAttribute(arguments[0], generateInterface);
            }
            else
                return new InterfaceAttribute();
        }

        return FailAttribute(attributeSyntax, DLFailureType.NotImplemented, $"Attribute '{name}' not implemented");
    }

    private static string ParseAttributeArgument(AttributeArgumentSyntax argument)
    {
        if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is string stringValue)
            return stringValue;

        return argument.Expression.ToString().Trim('"');
    }

    private static string[] ParseAttributeStringArrayArgument(AttributeArgumentSyntax argument)
    {
        if (argument.Expression is not ArrayCreationExpressionSyntax and not ImplicitArrayCreationExpressionSyntax)
            return [ParseAttributeArgument(argument)];

        var initializer = argument.Expression switch
        {
            ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Initializer,
            ImplicitArrayCreationExpressionSyntax implicitArrayCreation => implicitArrayCreation.Initializer,
            _ => null
        };

        if (initializer == null)
            return [];

        return initializer.Expressions
            .Select(expression => expression is LiteralExpressionSyntax literal && literal.Token.Value is string stringValue
                ? stringValue
                : expression.ToString().Trim('"'))
            .ToArray();
    }

    private static Option<Attribute, IDLOptionFailure> FailAttribute(AttributeSyntax attributeSyntax, DLFailureType type, string message)
        => TryGetSourceLocation(attributeSyntax, out var sourceLocation)
            ? DLOptionFailure.Fail(type, message, sourceLocation)
            : DLOptionFailure.Fail(type, message);

    private static bool TryParseBool(string value, out bool result) =>
        bool.TryParse(value, out result);

    private static bool TryParseLong(string value, out long result) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseReferentialAction(string value, out ReferentialAction result) =>
        Enum.TryParse(value.Split('.').Last(), out result);

    private static bool TryParseUlong(string value, out ulong result) =>
        ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseUint(string value, out uint result) =>
        uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryGetSourceLocation(SyntaxNode syntaxNode, out SourceLocation sourceLocation)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            sourceLocation = default;
            return false;
        }

        var file = new CsFileDeclaration(filePath);
        sourceLocation = new SourceLocation(file, new SourceTextSpan(syntaxNode.SpanStart, syntaxNode.Span.Length));
        return true;
    }

    public Option<PropertyDefinition, IDLOptionFailure> ParseProperty(PropertyDeclarationSyntax propSyntax, ModelDefinition model)
    {
        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in propSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes for {propSyntax.Identifier.Text}", model, failures);

        PropertyDefinition property = parsedAttributes.Any(attribute => attribute is RelationAttribute)
            ? new RelationProperty(propSyntax.Identifier.Text, new CsTypeDeclaration(propSyntax), model, parsedAttributes)
            : new ValueProperty(propSyntax.Identifier.Text, new CsTypeDeclaration(propSyntax), model, parsedAttributes);

        property.SetCsNullableCore(propSyntax.Type is NullableTypeSyntax);
        property.SetSourceInfoCore(new PropertySourceInfo(
            new SourceTextSpan(propSyntax.SpanStart, propSyntax.Span.Length),
            GetDefaultValueExpressionSourceSpan(propSyntax)));
        foreach (var (attribute, sourceSpan) in attributeSourceSpans)
            property.SetAttributeSourceSpanCore(attribute, sourceSpan);

        if (property is ValueProperty valueProp)
        {
            var enumAttribute = parsedAttributes.OfType<EnumAttribute>().SingleOrDefault();
            var propertyTypeName = valueProp.CsType.Name;
            var parentClassSyntax = propSyntax.Parent as TypeDeclarationSyntax;
            var enumDeclaration = FindEnumDeclaration(propertyTypeName);

            if (enumAttribute != null || enumDeclaration != null)
            {
                valueProp.SetCsSizeCore(MetadataTypeConverter.CsTypeSize("enum"));

                var enumValueList = enumAttribute?.Values.Select((x, i) => (x, i + 1)) ?? [];

                var declaredInClass = parentClassSyntax?.Members
                    .OfType<EnumDeclarationSyntax>()
                    .Any(enumDecl => enumDecl.Identifier.ValueText == propertyTypeName) ?? false;

                var csEnumValues = new List<(string name, int value)>();

                if (enumDeclaration != null)
                {
                    int lastValue = -1;
                    foreach (var member in enumDeclaration.Members)
                    {
                        if (member.EqualsValue != null)
                        {
                            var explicitValue = member.EqualsValue.Value.ToString();
                            if (!TryParseInt(explicitValue, out lastValue))
                                return FailProperty(member.EqualsValue.Value, DLFailureType.InvalidArgument, $"Invalid enum value '{explicitValue}' for enum member '{member.Identifier.ValueText}'. DataLinq enum metadata currently supports explicit integer values only.");
                        }
                        else
                        {
                            lastValue++;
                        }

                        csEnumValues.Add((member.Identifier.ValueText, lastValue));
                    }
                }

                valueProp.SetEnumPropertyCore(new EnumProperty(enumValueList, csEnumValues, declaredInClass));
            }
            else
            {
                valueProp.SetCsSizeCore(MetadataTypeConverter.CsTypeSize(property.CsType.Name));
            }
        }

        return property;
    }

    private Option<object, IDLOptionFailure> ParsePropertyDraft(PropertyDeclarationSyntax propSyntax)
    {
        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in propSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes for {propSyntax.Identifier.Text}", failures);

        var csType = new CsTypeDeclaration(propSyntax);
        var sourceInfo = new PropertySourceInfo(
            new SourceTextSpan(propSyntax.SpanStart, propSyntax.Span.Length),
            GetDefaultValueExpressionSourceSpan(propSyntax));

        if (parsedAttributes.Any(attribute => attribute is RelationAttribute))
        {
            return new MetadataRelationPropertyDraft(propSyntax.Identifier.Text, csType)
            {
                Attributes = parsedAttributes,
                AttributeSourceSpans = attributeSourceSpans,
                SourceInfo = sourceInfo,
                CsNullable = propSyntax.Type is NullableTypeSyntax,
                RelationName = parsedAttributes
                    .OfType<RelationAttribute>()
                    .FirstOrDefault()?
                    .Name
            };
        }

        int? csSize;
        EnumProperty? enumProperty = null;
        var enumAttribute = parsedAttributes.OfType<EnumAttribute>().SingleOrDefault();
        var propertyTypeName = csType.Name;
        var parentClassSyntax = propSyntax.Parent as TypeDeclarationSyntax;
        var enumDeclaration = FindEnumDeclaration(propertyTypeName);

        if (enumAttribute != null || enumDeclaration != null)
        {
            csSize = MetadataTypeConverter.CsTypeSize("enum");

            var enumValueList = enumAttribute?.Values.Select((x, i) => (x, i + 1)) ?? [];
            var declaredInClass = parentClassSyntax?.Members
                .OfType<EnumDeclarationSyntax>()
                .Any(enumDecl => enumDecl.Identifier.ValueText == propertyTypeName) ?? false;
            var csEnumValues = new List<(string name, int value)>();

            if (enumDeclaration != null)
            {
                int lastValue = -1;
                foreach (var member in enumDeclaration.Members)
                {
                    if (member.EqualsValue != null)
                    {
                        var explicitValue = member.EqualsValue.Value.ToString();
                        if (!TryParseInt(explicitValue, out lastValue))
                            return FailProperty(member.EqualsValue.Value, DLFailureType.InvalidArgument, $"Invalid enum value '{explicitValue}' for enum member '{member.Identifier.ValueText}'. DataLinq enum metadata currently supports explicit integer values only.");
                    }
                    else
                    {
                        lastValue++;
                    }

                    csEnumValues.Add((member.Identifier.ValueText, lastValue));
                }
            }

            enumProperty = new EnumProperty(enumValueList, csEnumValues, declaredInClass);
        }
        else
        {
            csSize = MetadataTypeConverter.CsTypeSize(csType.Name);
        }

        return new MetadataValuePropertyDraft(
            propSyntax.Identifier.Text,
            csType,
            ParseColumnDraft(propSyntax.Identifier.Text, parsedAttributes))
        {
            Attributes = parsedAttributes,
            AttributeSourceSpans = attributeSourceSpans,
            SourceInfo = sourceInfo,
            CsNullable = propSyntax.Type is NullableTypeSyntax,
            CsSize = csSize,
            EnumProperty = enumProperty
        };
    }

    private static MetadataColumnDraft ParseColumnDraft(string propertyName, IEnumerable<Attribute> attributes)
    {
        var columnName = propertyName;
        foreach (var attribute in attributes)
        {
            if (attribute is ColumnAttribute columnAttribute)
                columnName = columnAttribute.Name;
        }

        return new MetadataColumnDraft(columnName)
        {
            Nullable = attributes.Any(x => x is NullableAttribute),
            AutoIncrement = attributes.Any(x => x is AutoIncrementAttribute),
            PrimaryKey = attributes.Any(x => x is PrimaryKeyAttribute),
            ForeignKey = attributes.Any(x => x is ForeignKeyAttribute),
            DbTypes = attributes
                .OfType<TypeAttribute>()
                .Select(x => new DatabaseColumnType(x.DatabaseType, x.Name, x.Length, x.Decimals, x.Signed))
                .ToArray()
        };
    }

    private EnumDeclarationSyntax? FindEnumDeclaration(string enumName)
    {
        var allEnums = modelSyntaxes
            .SelectMany(x => x.DescendantNodesAndSelf().OfType<EnumDeclarationSyntax>())
            .Concat(enumSyntaxes);

        return allEnums.FirstOrDefault(e => e.Identifier.ValueText == enumName);
    }

    private static SourceTextSpan? GetDefaultValueExpressionSourceSpan(PropertyDeclarationSyntax propSyntax)
    {
        var defaultExpression = propSyntax.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(attr =>
            {
                var name = attr.Name.ToString();
                return name == "Default" || name == "DefaultAttribute";
            })
            .Select(attr => attr.ArgumentList?.Arguments.SingleOrDefault()?.Expression)
            .FirstOrDefault(expr => expr != null);

        if (defaultExpression == null)
            return null;

        return new SourceTextSpan(defaultExpression.SpanStart, defaultExpression.Span.Length);
    }

    public Option<(string csPropertyName, TypeDeclarationSyntax classSyntax), IDLOptionFailure> GetTableType(
        PropertyDeclarationSyntax property,
        List<TypeDeclarationSyntax> modelTypeSyntaxes,
        bool allowMissingTableModel = true)
    {
        var propType = property.Type;

        if (propType is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
        {
            if (genericType.TypeArgumentList.Arguments.Count != 1)
                return FailProperty(property, DLFailureType.InvalidModel, $"Table property '{property.Identifier.Text}' must use DbRead with exactly one model type argument.");

            if (genericType.TypeArgumentList.Arguments[0] is IdentifierNameSyntax typeArgument)
            {
                var modelClass = modelTypeSyntaxes.FirstOrDefault(cls => cls.Identifier.Text == typeArgument.Identifier.Text);
                if (modelClass == null && !allowMissingTableModel)
                    return FailProperty(property, DLFailureType.InvalidModel, $"Table property '{property.Identifier.Text}' references model '{typeArgument.Identifier.Text}', but no matching table or view model declaration was found.");

                return (property.Identifier.Text, modelClass);
            }

            return FailProperty(property, DLFailureType.InvalidModel, $"Table property '{property.Identifier.Text}' must use a simple model type argument. Found '{genericType.TypeArgumentList.Arguments[0]}'.");
        }

        return FailProperty(property, DLFailureType.NotImplemented, $"Table type {propType} is not implemented.");
    }

    private static IDLOptionFailure FailProperty(SyntaxNode syntaxNode, DLFailureType type, string message)
        => TryGetSourceLocation(syntaxNode, out var sourceLocation)
            ? DLOptionFailure.Fail(type, message, sourceLocation)
            : DLOptionFailure.Fail(type, message);

    private static IDLOptionFailure FailType(SyntaxNode syntaxNode, DLFailureType type, string message) =>
        TryGetSourceLocation(syntaxNode, out var sourceLocation)
            ? DLOptionFailure.Fail(type, message, sourceLocation)
            : DLOptionFailure.Fail(type, message);

    private static IDLOptionFailure FailModelDraft(MetadataModelDraft model, DLFailureType type, string message)
    {
        if (model.CsFile.HasValue)
            return DLOptionFailure.Fail(type, message, new SourceLocation(model.CsFile.Value, model.SourceSpan));

        return DLOptionFailure.Fail(type, message);
    }

    private bool InheritsFrom(CsTypeDeclaration decl, string typeName)
    {
        var matchingSyntax = modelSyntaxes.FirstOrDefault(ts => ts.Identifier.Text == decl.Name);
        if (matchingSyntax == null)
            return false;

        // If the matching syntax has a BaseList, check each base type.
        if (matchingSyntax.BaseList != null)
        {
            foreach (var baseTypeSyntax in matchingSyntax.BaseList.Types)
            {
                // Create a CsTypeDeclaration for the base type.
                var baseDecl = new CsTypeDeclaration(baseTypeSyntax);

                // Direct match: if the baseDecl's name is the type of interest.
                if (baseDecl.Name == typeName || baseDecl.Name.StartsWith(typeName + "<"))
                    return true;

                // Recursively check if the base type inherits from the type.
                if (InheritsFrom(baseDecl, typeName))
                    return true;
            }
        }
        return false;
    }
}
