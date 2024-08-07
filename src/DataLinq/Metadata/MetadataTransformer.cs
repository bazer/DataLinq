﻿using System.Linq;
using DataLinq.Attributes;

namespace DataLinq.Metadata;

public struct MetadataTransformerOptions
{
    public bool RemoveInterfacePrefix { get; set; } = true;
    public bool UpdateConstraintNames { get; } = true;

    public MetadataTransformerOptions(bool removeInterfacePrefix = true, bool updateConstraintNames = true)
    {
        RemoveInterfacePrefix = removeInterfacePrefix;
        UpdateConstraintNames = updateConstraintNames;
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

    public void TransformDatabase(DatabaseMetadata srcMetadata, DatabaseMetadata destMetadata)
    {
        destMetadata.Attributes = srcMetadata.Attributes;
        destMetadata.UseCache = srcMetadata.UseCache;
        destMetadata.CacheLimits = srcMetadata.CacheLimits;
        destMetadata.IndexCache = srcMetadata.IndexCache;
        destMetadata.CacheCleanup = srcMetadata.CacheCleanup;

        foreach (var srcTable in srcMetadata.TableModels)
        {
            var destTable = destMetadata.TableModels.FirstOrDefault(x => x.Table.DbName == srcTable.Table.DbName);

            if (destTable == null)
            {
                //log($"Couldn't find table with name '{srcTable.Table.DbName}' in {nameof(destMetadata)}");
                continue;
            }

            TransformTable(srcTable, destTable);
            destTable.CsPropertyName = srcTable.CsPropertyName;
        }
    }

    public void TransformTable(TableModelMetadata srcTable, TableModelMetadata destTable)
    {
        //log($"Transforming model '{srcTable.Table.DbName}'");
        var modelCsTypeName = srcTable.Model.CsTypeName;

        if (options.RemoveInterfacePrefix && srcTable.Model.ModelCsType == ModelCsType.Interface)
        {
            if (modelCsTypeName.StartsWith("I") && !char.IsLower(modelCsTypeName[1]))
                modelCsTypeName = modelCsTypeName.Substring(1);
        }

        if (destTable.Model.CsTypeName != modelCsTypeName)
        {
            destTable.Model.CsTypeName = modelCsTypeName;

            //foreach (var enumProp in destTable.Model.Properties.OfType<EnumProperty>())
            //{
            //    enumProp.CsTypeName = modelCsTypeName + enumProp.CsName;
            //}
        }

        destTable.Model.Interfaces = new ModelInterface[] { new ModelInterface { CsType = srcTable.Model.CsType, CsTypeName = srcTable.Model.CsTypeName } };
        destTable.Model.Namespaces = srcTable.Model.Namespaces;

        foreach (var srcProperty in srcTable.Model.ValueProperties.Values)
        {
            var destProperty = destTable.Model.ValueProperties.Values.FirstOrDefault(x => x.Column?.DbName == srcProperty.Column?.DbName);

            if (destProperty == null)
            {
                //log($"Couldn't find property with name '{srcProperty.CsName}' in {destTable.Table.DbName}");
                continue;
            }

            if (srcProperty.EnumProperty != null)
            {
                destProperty.EnumProperty = srcProperty.EnumProperty;
            }

            destProperty.CsName = srcProperty.CsName;
            destProperty.CsType = srcProperty.CsType;
            destProperty.CsTypeName = srcProperty.CsTypeName;
            destProperty.CsNullable = srcProperty.CsNullable;
            destProperty.CsSize = srcProperty.CsSize;

            foreach (var srcAttribute in srcProperty.Attributes.OfType<TypeAttribute>())
            {
                if (!destProperty.Attributes.OfType<TypeAttribute>().Any(x => x.DatabaseType == srcAttribute.DatabaseType))
                    destProperty.Attributes.Add(new TypeAttribute(srcAttribute.DatabaseType, srcAttribute.Name, srcAttribute.Length, srcAttribute.Decimals, srcAttribute.Signed));
            }

            foreach (var srcDbType in srcProperty.Column.DbTypes)
            {
                if (!destProperty.Column.DbTypes.Any(x => x.DatabaseType == srcDbType.DatabaseType))
                {
                    destProperty.Column.AddDbType(new DatabaseColumnType
                    {
                        DatabaseType = srcDbType.DatabaseType,
                        Name = srcDbType.Name,
                        Length = srcDbType.Length,
                        Decimals = srcDbType.Decimals,
                        Signed = srcDbType.Signed
                    });
                }
            }
        }

        foreach (var srcProperty in srcTable.Model.RelationProperties.Values)
        {
            var destProperty = destTable.Model.RelationProperties.Values.FirstOrDefault(x =>
                srcProperty.Attributes.OfType<RelationAttribute>().Any(y => x.RelationPart?.GetOtherSide().ColumnIndex.Table.DbName == y.Table) &&
                srcProperty.Attributes.OfType<RelationAttribute>().Any(y => x.RelationPart?.GetOtherSide().ColumnIndex.Columns.All(z => y.Columns.Contains(z.DbName)) == true));

            if (destProperty == null)
            {
                //log($"Couldn't find property with name '{srcProperty.CsName}' in {destTable.Table.DbName}");
                continue;
            }

            destProperty.CsName = srcProperty.CsName;

            if (!options.UpdateConstraintNames && srcProperty.RelationPart != null)
                destProperty.RelationPart.Relation.ConstraintName = srcProperty.RelationPart.Relation.ConstraintName;
        }
    }
}
