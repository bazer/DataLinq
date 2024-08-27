using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DataLinq.Attributes;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Core.Factories;

public static class SyntaxParser
{
    private static readonly string[] modelInterfaceNames = ["IDatabaseModel", "ITableModel", "IViewModel"];
    private static readonly string[] customModelInterfaceName = ["ICustomDatabaseModel", "ICustomTableModel", "ICustomViewModel"];

    public static bool IsModelInterface(string interfaceName) =>
        modelInterfaceNames.Any(interfaceName.StartsWith);

    public static bool IsCustomModelInterface(string interfaceName) =>
        customModelInterfaceName.Any(interfaceName.StartsWith);

    public static TableModel ParseTableModel(DatabaseDefinition database, TypeDeclarationSyntax typeSyntax, string csPropertyName)
    {
        var model = typeSyntax == null
            ? new ModelDefinition
            {
                Database = database,
                CsType = new CsTypeDeclaration(csPropertyName, "", ModelCsType.Interface),
                //ModelCsType = ModelCsType.Interface,
                //CsTypeName = csPropertyName,
                Interfaces = [],
                Attributes = []
            }
             : ParseModel(database, typeSyntax);

        return new TableModel
        {
            IsStub = typeSyntax == null,
            Model = model,
            Table = ParseTable(model),
            CsPropertyName = csPropertyName
        };
    }

    public static ModelDefinition ParseModel(DatabaseDefinition database, TypeDeclarationSyntax typeSyntax)
    {
        var model = new ModelDefinition
        {
            Database = database,
            CsType = new CsTypeDeclaration(typeSyntax),
            //ModelCsType = ParseModelCsType(typeSyntax),
            //CsTypeName = typeSyntax.Identifier.Text,
            //CsNamespace = GetNamespace(typeSyntax),
            Attributes = typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(x => ParseAttribute(x)).ToArray(),
            Interfaces = typeSyntax.BaseList?.Types.Select(baseType => new CsTypeDeclaration(baseType)).ToArray() ?? []
        };

        if (model.CsType.ModelCsType == ModelCsType.Interface)
            model.CsType = new CsTypeDeclaration(MetadataTypeConverter.RemoveInterfacePrefix(model.CsType.Name), model.CsType.Namespace, model.CsType.ModelCsType);

        typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(attr => attr.Name.ToString() == "Column" || attr.Name.ToString() == "Relation"))
            .Select(prop => ParseProperty(prop, model))
        .ToList()
        .ForEach(model.AddProperty);

        model.Usings = typeSyntax.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(uds => uds?.Name?.ToString())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .OrderBy(ns => ns!.StartsWith("System"))
            .ThenBy(ns => ns)
            .Select(ns => new ModelUsing { FullNamespaceName = ns! })
            .ToArray();

        return model;
    }

    public static Attribute ParseAttribute(AttributeSyntax attributeSyntax)
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

    public static TableDefinition ParseTable(ModelDefinition model)
    {
        TableDefinition table = model.Interfaces.Any(x => x.Name.StartsWith("ITableModel") || x.Name.StartsWith("ICustomTableModel"))
            ? new TableDefinition()
            : new ViewDefinition();

        table.Model = model;
        table.Database = model.Database;
        table.DbName = model.CsType.Name;

        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                table.DbName = tableAttribute.Name;

            if (attribute is UseCacheAttribute useCache)
                table.UseCache = useCache.UseCache;

            if (attribute is CacheLimitAttribute cacheLimit)
                table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (table is ViewDefinition view && attribute is DefinitionAttribute definitionAttribute)
                view.Definition = definitionAttribute.Sql;
        }

        table.Columns = model.ValueProperties.Values
            .Select(x => table.ParseColumn(x))
            .ToArray();

        model.Table = table;

        return table;
    }

    public static PropertyDefinition ParseProperty(PropertyDeclarationSyntax propSyntax, ModelDefinition model)
    {
        var attributes = propSyntax.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Select(attrSyntax => ParseAttribute(attrSyntax))
            .ToList();

        var property = GetProperty(attributes);

        property.Model = model;
        property.CsName = propSyntax.Identifier.Text;
        property.CsTypeName = propSyntax.Type.ToString().Trim('?', '"', '\"');
        property.Attributes = attributes;

        if (property is ValueProperty valueProp)
        {
            valueProp.CsNullable = propSyntax.Type is NullableTypeSyntax;

            if (attributes.Any(attribute => attribute is EnumAttribute))
            {
                valueProp.CsSize = MetadataTypeConverter.CsTypeSize("enum");

                var enumValueList = attributes.OfType<EnumAttribute>().Single().Values.Select((x, i) => (x, i + 1)).ToList();
                valueProp.EnumProperty = new EnumProperty(enumValueList, null, true);
            }
            else
            {
                valueProp.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
            }
        }

        return property;
    }

    public static PropertyDefinition GetProperty(List<Attribute> attributes)
    {
        if (attributes.Any(attr => attr is RelationAttribute))
            return new RelationProperty();

        return new ValueProperty();
    }

    public static (string csPropertyName, TypeDeclarationSyntax classSyntax) GetTableType(PropertyDeclarationSyntax property, List<TypeDeclarationSyntax> modelTypeSyntaxes)
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
}
