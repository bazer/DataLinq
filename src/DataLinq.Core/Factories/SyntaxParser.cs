using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public class SyntaxParser
{
    private static readonly string[] modelInterfaceNames = ["IDatabaseModel", "ITableModel", "IViewModel", "IModelInstance"];
    private static readonly string[] customModelInterfaceName = ["ICustomDatabaseModel", "ICustomTableModel", "ICustomViewModel"];
    private readonly ImmutableArray<TypeDeclarationSyntax> modelSyntaxes;

    public static bool IsModelInterface(string interfaceName) =>
        modelInterfaceNames.Any(interfaceName.StartsWith);

    public static bool IsCustomModelInterface(string interfaceName) =>
        customModelInterfaceName.Any(interfaceName.StartsWith);

    public SyntaxParser(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes)
    {
        this.modelSyntaxes = modelSyntaxes;
    }

    public Option<TableModel, IDLOptionFailure> ParseTableModel(DatabaseDefinition database, TypeDeclarationSyntax typeSyntax, string csPropertyName)
    {
        ModelDefinition model;
        if (typeSyntax == null)
        {
            model = new ModelDefinition(new CsTypeDeclaration(csPropertyName, database.CsType.Namespace, ModelCsType.Interface));
        }
        else
        {
            if (!ParseModel(typeSyntax).TryUnwrap(out model, out var failure))
                return failure;
        }

        return new TableModel(csPropertyName, database, model, typeSyntax == null);
    }

    private Option<ModelDefinition, IDLOptionFailure> ParseModel(TypeDeclarationSyntax typeSyntax)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(typeSyntax));

        if (!typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(ParseAttribute).Transpose().TryUnwrap(out var attributes, out var failures))
            return DLOptionFailure.Fail($"Error parsing attributes for {model}", failures);

        model.SetAttributes(attributes);

        var modelInstanceInterfaces = model.Attributes
            .Where(x => x is GenerateInterfaceAttribute interfaceAttribute && interfaceAttribute.GenerateInterface)
            .Select(x => x as GenerateInterfaceAttribute)
            .Select(x => new CsTypeDeclaration(x?.Name ?? $"I{model.CsType.Name}", model.CsType.Namespace, ModelCsType.Interface))
            .ToList();

        if (modelInstanceInterfaces.GroupBy(x => x.Name).Any(x => x.Count() > 1))
            return DLOptionFailure.Fail(DLFailureType.InvalidArgument,
                $"Duplicate interface names {modelInstanceInterfaces.GroupBy(x => x.Name).Where(x => x.Count() > 1).ToJoinedString()} in model '{model.CsType.Name}'");

        if (modelInstanceInterfaces.Any())
            model.SetModelInstanceInterfaces(modelInstanceInterfaces);

        if (typeSyntax.BaseList != null)
        {
            // Build all interfaces from the BaseList.
            var interfaces = typeSyntax.BaseList.Types
                .Select(baseType => new CsTypeDeclaration(baseType))
                .Where(x => !x.Name.StartsWith("Immutable<"))
                .ToList();

            model.SetInterfaces(interfaces);
        }

        if (model.CsType.ModelCsType == ModelCsType.Interface)
            model.SetCsType(model.CsType.MutateName(MetadataTypeConverter.RemoveInterfacePrefix(model.CsType.Name)));

        if (!typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(attr => attr.Name.ToString() == "Column" || attr.Name.ToString() == "Relation"))
            .Select(prop => ParseProperty(prop, model))
            .Transpose()
            .TryUnwrap(out var properties, out var propFailures))
            return DLOptionFailure.Fail($"Error parsing properties in {model}", propFailures);

        model.AddProperties(properties);

        model.SetUsings(typeSyntax.SyntaxTree.GetRoot()
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

    public Option<Attribute, IDLOptionFailure> ParseAttribute(AttributeSyntax attributeSyntax)
    {
        var name = attributeSyntax.Name.ToString();
        var arguments = attributeSyntax.ArgumentList?.Arguments
            .Select(x => x.Expression.ToString().Trim('"'))
            .ToList() ?? [];

        if (name == "Database")
        {
            if (arguments.Count != 1)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new DatabaseAttribute(arguments[0]);
        }

        if (name == "Table")
        {
            if (arguments.Count != 1)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new TableAttribute(arguments[0]);
        }

        if (name == "View")
        {
            if (arguments.Count != 1)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new ViewAttribute(arguments[0]);
        }

        if (name == "Column")
        {
            if (arguments.Count != 1)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new ColumnAttribute(arguments[0]);
        }

        if (name == "Definition")
        {
            if (arguments.Count != 1)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new DefinitionAttribute(arguments[0]);
        }

        if (name == "UseCache")
        {
            if (arguments.Count == 1)
                return new UseCacheAttribute(bool.Parse(arguments[0]));
            else
                return new UseCacheAttribute();
        }

        if (name == "CacheLimit")
        {
            if (arguments.Count != 2)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheLimitType limitType))
                return DLOptionFailure.Fail(DLFailureType.InvalidType, $"Invalid CacheLimitType value '{arguments[0]}'");

            return new CacheLimitAttribute(limitType, long.Parse(arguments[1]));
        }

        if (name == "CacheCleanup")
        {
            if (arguments.Count != 2)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheCleanupType cleanupType))
                return DLOptionFailure.Fail(DLFailureType.InvalidType, $"Invalid CacheCleanupType value '{arguments[0]}'");

            return new CacheCleanupAttribute(cleanupType, long.Parse(arguments[1]));
        }

        if (name == "IndexCache")
        {
            if (arguments.Count < 1 || arguments.Count > 2)
            {
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 1 or 2 arguments");
            }

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out IndexCacheType indexCacheType))
            {
                return DLOptionFailure.Fail(DLFailureType.InvalidType, $"Invalid IndexCacheType value '{arguments[0]}'");
            }

            return arguments.Count == 1
                ? new IndexCacheAttribute(indexCacheType)
                : new IndexCacheAttribute(indexCacheType, int.Parse(arguments[1]));
        }

        if (name == "AutoIncrement")
        {
            if (arguments.Any())
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new AutoIncrementAttribute();
        }

        if (name == "Relation")
        {
            if (arguments.Count == 2)
                return new RelationAttribute(arguments[0], arguments[1]);
            else if (arguments.Count == 3)
                return new RelationAttribute(arguments[0], arguments[1], arguments[2]);
            else
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 or 3 arguments");
        }

        if (name == "PrimaryKey")
        {
            if (arguments.Any())
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new PrimaryKeyAttribute();
        }

        if (name == "ForeignKey")
        {
            if (arguments.Count != 3)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' must have 3 arguments");

            return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2]);
        }

        if (name == "Enum")
        {
            return new EnumAttribute([.. arguments]);
        }

        if (name == "Nullable")
        {
            if (arguments.Any())
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new NullableAttribute();
        }

        if (name == "Default")
        {
            if (arguments.Count != 1)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            return new DefaultAttribute(arguments[0]);
        }

        if (name == "DefaultCurrentTimestamp")
        {
            if (arguments.Count != 0)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new DefaultCurrentTimestampAttribute();
        }

        if (name == "Index")
        {
            if (arguments.Count < 2)
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            string indexName = arguments[0];
            if (!Enum.TryParse(arguments[1].Split('.').Last(), out IndexCharacteristic characteristic))
                return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Invalid IndexCharacteristic value '{arguments[1]}'");

            if (arguments.Count == 2)
                return new IndexAttribute(arguments[0], characteristic);

            if (Enum.TryParse(arguments[2].Split('.').Last(), out IndexType type))
                return new IndexAttribute(indexName, characteristic, type, arguments.Skip(3).ToArray());
            else
                return new IndexAttribute(indexName, characteristic, arguments.Skip(2).ToArray());
        }


        if (name == "Type")
        {
            string enumValue = arguments[0].Split('.').Last();
            if (Enum.TryParse(enumValue, out DatabaseType dbType))
            {
                switch (arguments.Count)
                {
                    case 1: return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");
                    case 2: return new TypeAttribute(dbType, arguments[1]);
                    case 3:
                        if (long.TryParse(arguments[2], out long length))
                            return new TypeAttribute(dbType, arguments[1], length);
                        else
                            return new TypeAttribute(dbType, arguments[1], bool.Parse(arguments[2]));
                    case 4:
                        if (int.TryParse(arguments[3], out int decimals))
                            return new TypeAttribute(dbType, arguments[1], long.Parse(arguments[2]), decimals);
                        else
                            return new TypeAttribute(dbType, arguments[1], long.Parse(arguments[2]), bool.Parse(arguments[3]));
                    case 5:
                        return new TypeAttribute(dbType, arguments[1], long.Parse(arguments[2]), int.Parse(arguments[3]), bool.Parse(arguments[4]));
                }
            }
            else
            {
                switch (arguments.Count)
                {
                    case 1: return new TypeAttribute(arguments[0]);
                    case 2:
                        if (long.TryParse(arguments[1], out long length))
                            return new TypeAttribute(arguments[0], length);
                        else
                            return new TypeAttribute(arguments[0], bool.Parse(arguments[1]));
                    case 3:
                        return new TypeAttribute(arguments[0], long.Parse(arguments[1]), bool.Parse(arguments[2]));
                    case 4:
                        return new TypeAttribute(arguments[0], long.Parse(arguments[1]), int.Parse(arguments[2]), bool.Parse(arguments[3]));
                }
            }

            return DLOptionFailure.Fail(DLFailureType.NotImplemented, $"Attribute 'TypeAttribute' with {arguments.Count} arguments not implemented");
        }

        if (name == "GenerateInterface")
        {
            if (arguments.Count == 1)
                return new GenerateInterfaceAttribute(arguments[0]);
            else if (arguments.Count == 2)
                return new GenerateInterfaceAttribute(arguments[0], bool.Parse(arguments[1]));
            else
                return new GenerateInterfaceAttribute();
        }

        return DLOptionFailure.Fail(DLFailureType.NotImplemented, $"Attribute '{name}' not implemented");
    }

    public Option<PropertyDefinition, IDLOptionFailure> ParseProperty(PropertyDeclarationSyntax propSyntax, ModelDefinition model)
    {
        if (!propSyntax.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Select(ParseAttribute)
            .Transpose()
            .TryUnwrap(out var attributes, out var failures))
            return DLOptionFailure.Fail($"Error parsing attributes for {model}", failures);

        PropertyDefinition property = attributes.Any(attribute => attribute is RelationAttribute)
            ? new RelationProperty(propSyntax.Identifier.Text, new CsTypeDeclaration(propSyntax), model, attributes)
            : new ValueProperty(propSyntax.Identifier.Text, new CsTypeDeclaration(propSyntax), model, attributes);

        if (property is ValueProperty valueProp)
        {
            valueProp.SetCsNullable(propSyntax.Type is NullableTypeSyntax);

            if (attributes.Any(attribute => attribute is EnumAttribute))
            {
                valueProp.SetCsSize(MetadataTypeConverter.CsTypeSize("enum"));

                var enumValueList = attributes.OfType<EnumAttribute>().Single().Values.Select((x, i) => (x, i + 1)).ToList();
                valueProp.SetEnumProperty(new EnumProperty(enumValueList, null, true));
            }
            else
            {
                valueProp.SetCsSize(MetadataTypeConverter.CsTypeSize(property.CsType.Name));
            }
        }

        return property;
    }

    public Option<(string csPropertyName, TypeDeclarationSyntax classSyntax), IDLOptionFailure> GetTableType(PropertyDeclarationSyntax property, List<TypeDeclarationSyntax> modelTypeSyntaxes)
    {
        var propType = property.Type;

        if (propType is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
        {
            var typeArgument = genericType.TypeArgumentList.Arguments[0] as IdentifierNameSyntax;
            if (typeArgument != null)
            {
                var modelClass = modelTypeSyntaxes.FirstOrDefault(cls => cls.Identifier.Text == typeArgument.Identifier.Text);
                return (property.Identifier.Text, modelClass);
            }
        }

        return DLOptionFailure.Fail(DLFailureType.NotImplemented, $"Table type {propType} is not implemented.");
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
