using System;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Core.Factories;

public struct MetadataTransformerOptions
{
    public bool RemoveInterfacePrefix { get; set; } = true;
    public bool UpdateConstraintNames { get; set; } = true;
    public bool OverwritePropertyTypes { get; set; } = false;

    public MetadataTransformerOptions(bool removeInterfacePrefix = true, bool updateConstraintNames = true, bool overwritePropertyTypes = false)
    {
        RemoveInterfacePrefix = removeInterfacePrefix;
        UpdateConstraintNames = updateConstraintNames;
        OverwritePropertyTypes = overwritePropertyTypes;
    }
}

public class MetadataTransformer
{
    //private readonly Action<string> log;
    private readonly MetadataTransformerOptions options;

    public MetadataTransformer(MetadataTransformerOptions options)
    {
        //this.log = log;
        this.options = options;
    }

    private static CsTypeDeclaration TransformCsType(CsTypeDeclaration srcCsType, CsTypeDeclaration destCsType, bool removeInterfacePrefix = true)
    {
        var modelCsTypeName = srcCsType.Name;

        if (removeInterfacePrefix && srcCsType.ModelCsType == ModelCsType.Interface)
        {
            if (modelCsTypeName.StartsWith("I") && !char.IsLower(modelCsTypeName[1]))
                modelCsTypeName = modelCsTypeName.Substring(1);
        }

        if (destCsType.Name != modelCsTypeName)
            return destCsType
                .MutateName(modelCsTypeName)
                .MutateNamespace(srcCsType.Namespace);

        return destCsType;
    }

    public void TransformDatabase(DatabaseDefinition srcMetadata, DatabaseDefinition destMetadata)
    {
        destMetadata.SetAttributes(srcMetadata.Attributes);
        destMetadata.SetCache(srcMetadata.UseCache);
        destMetadata.CacheLimits.Clear();
        destMetadata.CacheLimits.AddRange(srcMetadata.CacheLimits);
        destMetadata.IndexCache.Clear();
        destMetadata.IndexCache.AddRange(srcMetadata.IndexCache);
        destMetadata.CacheCleanup.Clear();
        destMetadata.CacheCleanup.AddRange(srcMetadata.CacheCleanup);

        destMetadata.SetCsType(TransformCsType(srcMetadata.CsType, destMetadata.CsType));

        foreach (var srcTable in srcMetadata.TableModels)
        {
            var destTable = destMetadata.TableModels.FirstOrDefault(x => x.Table.DbName == srcTable.Table.DbName);

            if (destTable == null)
            {
                //log($"Couldn't find table with name '{srcTable.Table.DbName}' in {nameof(destMetadata)}");
                continue;
            }

            TransformTable(srcTable, destTable);
            destTable.SetCsPropertyName(srcTable.CsPropertyName);
        }
    }

    public void TransformTable(TableModel srcTable, TableModel destTable)
    {
        destTable.Model.SetCsType(TransformCsType(srcTable.Model.CsType, destTable.Model.CsType));

        if (srcTable.Model.ModelInstanceInterface != null)
            destTable.Model.SetModelInstanceInterface(srcTable.Model.ModelInstanceInterface);
        else
        {
            var interfaceName = $"I{destTable.Model.CsType.Name}";
            destTable.Model.SetModelInstanceInterface(new CsTypeDeclaration(interfaceName, destTable.Model.CsType.Namespace, ModelCsType.Interface));
        }


        //destTable.Model.SetInterfaces([srcTable.Model.CsType]); //TODO: Investigate if this is needed
        destTable.Model.SetUsings(srcTable.Model.Usings);

        foreach (var srcProperty in srcTable.Model.ValueProperties.Values)
        {
            var destPropertyKeyValue = destTable.Model.ValueProperties.FirstOrDefault(x => x.Value.Column?.DbName == srcProperty.Column?.DbName);
            var key = destPropertyKeyValue.Key;
            var destProperty = destPropertyKeyValue.Value;

            if (destProperty == null)
            {
                //log($"Couldn't find property with name '{srcProperty.CsName}' in {destTable.Table.DbName}");
                continue;
            }

            // Check if the property name has changed and update the key
            if (key != srcProperty.PropertyName)
            {
                destTable.Model.ValueProperties.Remove(key);
                destTable.Model.ValueProperties.Add(srcProperty.PropertyName, destProperty);
            }

            destProperty.SetPropertyName(srcProperty.PropertyName);

            if (srcProperty.EnumProperty != null)
                destProperty.SetEnumProperty(srcProperty.EnumProperty.Value);

            // Only apply the type information from the source file IF:
            // 1. The overwrite option is OFF, OR
            // 2. The source property is an ENUM (we always want to preserve enums), OR
            // 3. The source property's C# type is NOT a simple, known type (i.e., it's a custom user type).
            if (!options.OverwritePropertyTypes ||
                srcProperty.EnumProperty != null ||
                !MetadataTypeConverter.IsKnownCsType(srcProperty.CsType.Name))
            {
                destProperty.SetCsType(srcProperty.CsType);
                destProperty.SetCsNullable(srcProperty.CsNullable);
                destProperty.SetCsSize(srcProperty.CsSize);
            }

            foreach (var srcAttribute in srcProperty.Attributes.OfType<TypeAttribute>())
            {
                if (!destProperty.Attributes.OfType<TypeAttribute>().Any(x => x.DatabaseType == srcAttribute.DatabaseType))
                    destProperty.AddAttribute(new TypeAttribute(srcAttribute.DatabaseType, srcAttribute.Name, srcAttribute.Length, srcAttribute.Decimals, srcAttribute.Signed));
            }

            foreach (var srcDbType in srcProperty.Column.DbTypes)
            {
                if (!destProperty.Column.DbTypes.Any(x => x.DatabaseType == srcDbType.DatabaseType))
                {
                    destProperty.Column.AddDbType(srcDbType.Clone());
                }
            }
        }

        // Create a stable key for a relation based on the DB columns it connects.
        // Example key: "users.id->orders.user_id"
        Func<RelationPart, string> stableKeyGenerator = (part) =>
        {
            var fkCols = string.Join(",", part.ColumnIndex.Columns.Select(c => c.DbName));
            var pkCols = string.Join(",", part.GetOtherSide().ColumnIndex.Columns.Select(c => c.DbName));
            return $"{part.GetOtherSide().ColumnIndex.Table.DbName}.({pkCols})->{part.ColumnIndex.Table.DbName}.({fkCols})";
        };

        // Create a lookup dictionary of the original destination relation properties, keyed by the stable column key.
        var oldDestRelationsMap = destTable.Model.RelationProperties.Values
            .Where(p => p.RelationPart != null) // Filter out null RelationPart entries
            .ToDictionary(p => stableKeyGenerator(p.RelationPart), p => p);

        destTable.Model.RelationProperties.Clear();

        // Now, iterate through the source properties, which are the "source of truth" for C# names.
        foreach (var srcProperty in srcTable.Model.RelationProperties.Values)
        {
            if (srcProperty.RelationPart == null) continue; // Cannot process relations without a RelationPart

            var newRelationProperty = new RelationProperty(
                srcProperty.PropertyName,
                srcProperty.CsType,
                destTable.Model,
                srcProperty.Attributes
            );

            // Find the corresponding original destination property using the stable key.
            var stableKey = stableKeyGenerator(srcProperty.RelationPart);
            oldDestRelationsMap.TryGetValue(stableKey, out var originalDestProperty);

            // Start with the source's RelationPart as the default. It contains the correct C# property info.
            var partToAssign = srcProperty.RelationPart;

            // If the option is to preserve names, AND we found a corresponding destination part...
            if (!options.UpdateConstraintNames && originalDestProperty?.RelationPart != null)
            {
                // ...then we use the destination's part, because it holds the constraint name we want to preserve.
                partToAssign = originalDestProperty.RelationPart;
            }

            newRelationProperty.SetRelationPart(partToAssign);
            newRelationProperty.SetRelationName(partToAssign.Relation.ConstraintName);
            destTable.Model.AddProperty(newRelationProperty);
        }
    }
}
