using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    public TableModel ParseTableModel(DatabaseDefinition database, TypeDeclarationSyntax typeSyntax, string csPropertyName)
    {
        var model = typeSyntax == null
            ? new ModelDefinition(new CsTypeDeclaration(csPropertyName, database.CsType.Namespace, ModelCsType.Interface))
            : ParseModel(typeSyntax);

        return new TableModel(csPropertyName, database, model, typeSyntax == null);
    }

    private ModelDefinition ParseModel(TypeDeclarationSyntax typeSyntax)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(typeSyntax));

        model.SetAttributes(typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(ParseAttribute));
        if (typeSyntax.BaseList != null)
        {
            // Build all interfaces from the BaseList.
            var interfaces = typeSyntax.BaseList.Types
                .Select(baseType => new CsTypeDeclaration(baseType))
                .Where(x => !x.Name.StartsWith("Immutable<"))
                .ToList();

            var modelInstanceInterfaces = interfaces
                .Where(x => InheritsFrom(x, "IModelInstance"))
                .ToList();

            if (!modelInstanceInterfaces.Any())
            {
                var newInterface = new CsTypeDeclaration($"I{model.CsType.Name}", model.CsType.Namespace, ModelCsType.Interface);
                interfaces.Add(newInterface);
                modelInstanceInterfaces.Add(newInterface);
            }

            model.SetInterfaces(interfaces);
            model.SetModelInstanceInterfaces(modelInstanceInterfaces);
        }

        if (model.CsType.ModelCsType == ModelCsType.Interface)
            model.SetCsType(model.CsType.MutateName(MetadataTypeConverter.RemoveInterfacePrefix(model.CsType.Name)));

        typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(attr => attr.Name.ToString() == "Column" || attr.Name.ToString() == "Relation"))
            .Select(prop => ParseProperty(prop, model))
        .ToList()
        .ForEach(model.AddProperty);

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

    public Attribute ParseAttribute(AttributeSyntax attributeSyntax)
    {
        var name = attributeSyntax.Name.ToString();
        var arguments = attributeSyntax.ArgumentList?.Arguments
            .Select(x => x.Expression.ToString().Trim('"'))
            .ToList() ?? new();

        if (name == "Database")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new DatabaseAttribute(arguments[0]);
        }

        if (name == "Table")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new TableAttribute(arguments[0]);
        }

        if (name == "View")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new ViewAttribute(arguments[0]);
        }

        if (name == "Column")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new ColumnAttribute(arguments[0]);
        }

        if (name == "Definition")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

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
                throw new ArgumentException($"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheLimitType limitType))
                throw new ArgumentException($"Invalid CacheLimitType value '{arguments[0]}'");

            return new CacheLimitAttribute(limitType, long.Parse(arguments[1]));
        }

        if (name == "CacheCleanup")
        {
            if (arguments.Count != 2)
                throw new ArgumentException($"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheCleanupType cleanupType))
                throw new ArgumentException($"Invalid CacheCleanupType value '{arguments[0]}'");

            return new CacheCleanupAttribute(cleanupType, long.Parse(arguments[1]));
        }

        if (name == "IndexCache")
        {
            if (arguments.Count < 1 || arguments.Count > 2)
            {
                throw new ArgumentException($"Attribute '{name}' doesn't have 1 or 2 arguments");
            }

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out IndexCacheType indexCacheType))
            {
                throw new ArgumentException($"Invalid IndexCacheType value '{arguments[0]}'");
            }

            return arguments.Count == 1
                ? new IndexCacheAttribute(indexCacheType)
                : new IndexCacheAttribute(indexCacheType, int.Parse(arguments[1]));
        }

        if (name == "AutoIncrement")
        {
            if (arguments.Any())
                throw new ArgumentException($"Attribute '{name}' have too many arguments");

            return new AutoIncrementAttribute();
        }

        if (name == "Relation")
        {
            if (arguments.Count == 2)
                return new RelationAttribute(arguments[0], arguments[1]);
            else if (arguments.Count == 3)
                return new RelationAttribute(arguments[0], arguments[1], arguments[2]);
            else
                throw new ArgumentException($"Attribute '{name}' doesn't have 2 or 3 arguments");
        }

        if (name == "PrimaryKey")
        {
            if (arguments.Any())
                throw new ArgumentException($"Attribute '{name}' have too many arguments");

            return new PrimaryKeyAttribute();
        }

        if (name == "ForeignKey")
        {
            if (arguments.Count != 3)
                throw new ArgumentException($"Attribute '{name}' must have 3 arguments");

            return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2]);
        }

        if (name == "Enum")
        {
            return new EnumAttribute(arguments.ToArray());
        }

        if (name == "Nullable")
        {
            if (arguments.Any())
                throw new ArgumentException($"Attribute '{name}' have too many arguments");

            return new NullableAttribute();
        }

        if (name == "Default")
        {
            if (arguments.Count != 2)
                throw new ArgumentException($"Attribute '{name}' have too few arguments");

            string enumValue = arguments[0].Split('.').Last();
            if (!Enum.TryParse(enumValue, out DatabaseType dbType))
                throw new ArgumentException($"Invalid DatabaseType value '{arguments[0]}'");

            return new DefaultAttribute(dbType, arguments[1]);
        }

        if (name == "Index")
        {
            if (arguments.Count < 2)
                throw new ArgumentException($"Attribute '{name}' have too few arguments");

            string indexName = arguments[0];
            if (!Enum.TryParse(arguments[1].Split('.').Last(), out IndexCharacteristic characteristic))
                throw new ArgumentException($"Invalid IndexCharacteristic value '{arguments[1]}'");

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
                    case 1: throw new ArgumentException($"Attribute '{name}' have too few arguments");
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

            throw new NotImplementedException($"Attribute 'TypeAttribute' with {arguments.Count} arguments not implemented");
        }

        throw new NotImplementedException($"Attribute '{name}' not implemented");
    }

    public PropertyDefinition ParseProperty(PropertyDeclarationSyntax propSyntax, ModelDefinition model)
    {
        var attributes = propSyntax.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Select(ParseAttribute)
            .ToList();

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

    public (string csPropertyName, TypeDeclarationSyntax classSyntax) GetTableType(PropertyDeclarationSyntax property, List<TypeDeclarationSyntax> modelTypeSyntaxes)
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
        throw new NotImplementedException();
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
