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

    public DatabaseDefinition TransformDatabaseSnapshot(DatabaseDefinition srcMetadata, DatabaseDefinition destMetadata)
    {
        var transformedMetadata = MetadataDefinitionSnapshot.Copy(destMetadata);
        TransformDatabaseInPlace(srcMetadata, transformedMetadata);

        return transformedMetadata;
    }

    [Obsolete("Use TransformDatabaseSnapshot to return a merged metadata graph without mutating the provider-derived destination metadata.")]
    public void TransformDatabase(DatabaseDefinition srcMetadata, DatabaseDefinition destMetadata)
    {
        TransformDatabaseInPlace(srcMetadata, destMetadata);
    }

    private void TransformDatabaseInPlace(DatabaseDefinition srcMetadata, DatabaseDefinition destMetadata)
    {
        destMetadata.SetAttributesCore(srcMetadata.Attributes);
        destMetadata.SetCacheCore(srcMetadata.UseCache);
        destMetadata.CacheLimits.ClearCore();
        destMetadata.CacheLimits.AddRangeCore(srcMetadata.CacheLimits);
        destMetadata.IndexCache.ClearCore();
        destMetadata.IndexCache.AddRangeCore(srcMetadata.IndexCache);
        destMetadata.CacheCleanup.ClearCore();
        destMetadata.CacheCleanup.AddRangeCore(srcMetadata.CacheCleanup);

        destMetadata.SetCsTypeCore(TransformCsType(srcMetadata.CsType, destMetadata.CsType));

        foreach (var srcTable in srcMetadata.TableModels)
        {
            var destTable = destMetadata.TableModels.FirstOrDefault(x => x.Table.DbName == srcTable.Table.DbName);

            if (destTable == null)
            {
                //log($"Couldn't find table with name '{srcTable.Table.DbName}' in {nameof(destMetadata)}");
                continue;
            }

            TransformTableInPlace(srcTable, destTable);
            destTable.SetCsPropertyNameCore(srcTable.CsPropertyName);
        }
    }

    [Obsolete("Use TransformDatabaseSnapshot to merge metadata without direct table graph mutation.")]
    public void TransformTable(TableModel srcTable, TableModel destTable)
    {
        TransformTableInPlace(srcTable, destTable);
    }

    private void TransformTableInPlace(TableModel srcTable, TableModel destTable)
    {
        destTable.Model.SetCsTypeCore(TransformCsType(srcTable.Model.CsType, destTable.Model.CsType));
        if (srcTable.Model.CsFile != null)
            destTable.Model.SetCsFileCore(srcTable.Model.CsFile.Value);

        if (srcTable.Model.ModelInstanceInterface != null)
            destTable.Model.SetModelInstanceInterfaceCore(srcTable.Model.ModelInstanceInterface);
        else
        {
            var interfaceName = $"I{destTable.Model.CsType.Name}";
            destTable.Model.SetModelInstanceInterfaceCore(new CsTypeDeclaration(interfaceName, destTable.Model.CsType.Namespace, ModelCsType.Interface));
        }


        //destTable.Model.SetInterfacesCore([srcTable.Model.CsType]); //TODO: Investigate if this is needed
        destTable.Model.SetUsingsCore(srcTable.Model.Usings);

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
                destTable.Model.ValueProperties.RemoveCore(key);
                destTable.Model.ValueProperties.AddCore(srcProperty.PropertyName, destProperty);
            }

            destProperty.SetPropertyNameCore(srcProperty.PropertyName);
            if (srcProperty.SourceInfo != null)
                destProperty.SetSourceInfoCore(srcProperty.SourceInfo.Value);

            if (srcProperty.EnumProperty != null)
                destProperty.SetEnumPropertyCore(srcProperty.EnumProperty.Value);

            // Only apply the type information from the source file IF:
            // 1. The overwrite option is OFF, OR
            // 2. The source property is an ENUM (we always want to preserve enums), OR
            // 3. The source property's C# type is NOT a simple, known type (i.e., it's a custom user type).
            if (!options.OverwritePropertyTypes ||
                srcProperty.EnumProperty != null ||
                !MetadataTypeConverter.IsKnownCsType(srcProperty.CsType.Name))
            {
                destProperty.SetCsTypeCore(srcProperty.CsType);
                destProperty.SetCsNullableCore(srcProperty.CsNullable);
                destProperty.SetCsSizeCore(srcProperty.CsSize);
            }

            if (srcProperty.HasDefaultValue() &&
                (srcProperty.EnumProperty != null || !MetadataTypeConverter.IsKnownCsType(srcProperty.CsType.Name)))
            {
                var sourceDefault = srcProperty.GetDefaultAttribute();
                destProperty.SetAttributesCore(
                    destProperty.Attributes
                        .Where(x => x is not DefaultAttribute)
                        .Concat(sourceDefault != null ? [sourceDefault] : []));
            }

            foreach (var srcAttribute in srcProperty.Attributes.OfType<TypeAttribute>())
            {
                if (!destProperty.Attributes.OfType<TypeAttribute>().Any(x => x.DatabaseType == srcAttribute.DatabaseType))
                    destProperty.AddAttributeCore(new TypeAttribute(srcAttribute.DatabaseType, srcAttribute.Name, srcAttribute.Length, srcAttribute.Decimals, srcAttribute.Signed));
            }

            foreach (var srcDbType in srcProperty.Column.DbTypes)
            {
                if (!destProperty.Column.DbTypes.Any(x => x.DatabaseType == srcDbType.DatabaseType))
                {
                    destProperty.Column.AddDbTypeCore(srcDbType.Clone());
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
                var mergedRelationDefinition = new RelationDefinition(constraintName, destRelation.RelationPart.Relation.Type);
                mergedRelationDefinition.SetForeignKeyCore(destRelation.RelationPart.Relation.ForeignKey);
                mergedRelationDefinition.SetCandidateKeyCore(destRelation.RelationPart.Relation.CandidateKey);
                mergedRelationDefinition.SetOnUpdateCore(destRelation.RelationPart.Relation.OnUpdate);
                mergedRelationDefinition.SetOnDeleteCore(destRelation.RelationPart.Relation.OnDelete);

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

                finalRelationProperty.SetRelationPartCore(finalRelationPart);
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
        destTable.Model.RelationProperties.ClearCore();
        destTable.Model.AddPropertiesCore(finalRelations);
    }
}
