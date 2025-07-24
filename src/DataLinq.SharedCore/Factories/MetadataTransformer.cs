using System;
using System.Collections.Generic;
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

        // Map all relations from the source (C# files)
        var srcRelationsMap = srcTable.Model.RelationProperties.Values
            .Where(p => p.RelationPart != null)
            .ToDictionary(p => stableKeyGenerator(p.RelationPart), p => p);

        // Map all relations from the destination (database schema)
        var destRelationsMap = destTable.Model.RelationProperties.Values
            .Where(p => p.RelationPart != null)
            .ToDictionary(p => stableKeyGenerator(p.RelationPart), p => p);

        var finalRelations = new List<RelationProperty>();

        // Iterate through all relations found in the database. This is the source of truth.
        foreach (var destRelation in destRelationsMap.Values)
        {
            var stableKey = stableKeyGenerator(destRelation.RelationPart);

            // RELATION EXISTS IN BOTH: Merge them.
            if (srcRelationsMap.TryGetValue(stableKey, out var srcRelation))
            {
                // Decide which constraint name to use based on the option
                var constraintName = options.UpdateConstraintNames
                    ? srcRelation.RelationPart.Relation.ConstraintName
                    : destRelation.RelationPart.Relation.ConstraintName;

                // Create a new, merged RelationDefinition
                var mergedRelationDefinition = new RelationDefinition(constraintName, destRelation.RelationPart.Relation.Type)
                {
                    ForeignKey = destRelation.RelationPart.Relation.ForeignKey,
                    CandidateKey = destRelation.RelationPart.Relation.CandidateKey
                };

                // Create the final RelationProperty using the C# name from the source
                var finalRelationProperty = new RelationProperty(
                    srcRelation.PropertyName,
                    destRelation.CsType, // Use the type from the DB for consistency
                    destTable.Model,
                    srcRelation.Attributes
                );

                // Create a new RelationPart with the merged definition and the C# name
                var finalRelationPart = new RelationPart(
                    destRelation.RelationPart.ColumnIndex,
                    mergedRelationDefinition,
                    destRelation.RelationPart.Type,
                    srcRelation.PropertyName
                );

                finalRelationProperty.SetRelationPart(finalRelationPart);
                finalRelations.Add(finalRelationProperty);
            }
            else
            {
                // NEW RELATION: It only exists in the database. Keep it as is.
                // The default property name generated by the DB parser will be used.
                finalRelations.Add(destRelation);
            }
        }

        // Replace the old relation properties with the new, correctly merged list.
        destTable.Model.RelationProperties.Clear();
        destTable.Model.AddProperties(finalRelations);
    }
}
