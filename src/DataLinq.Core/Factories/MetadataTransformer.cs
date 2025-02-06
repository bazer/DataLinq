using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Core.Factories;

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

    private static CsTypeDeclaration TransformCsType(CsTypeDeclaration srcCsType, CsTypeDeclaration destCsType, bool removeInterfacePrefix = true)
    {
        var modelCsTypeName = srcCsType.Name;

        if (removeInterfacePrefix && srcCsType.ModelCsType == ModelCsType.Interface)
        {
            if (modelCsTypeName.StartsWith("I") && !char.IsLower(modelCsTypeName[1]))
                modelCsTypeName = modelCsTypeName.Substring(1);
        }

        if (destCsType.Name != modelCsTypeName)
            return destCsType.MutateName(modelCsTypeName);

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

        //destTable.Model.SetInterfaces([srcTable.Model.CsType]); //TODO: Investigate if this is needed
        destTable.Model.SetUsings(srcTable.Model.Usings);

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
                destProperty.SetEnumProperty(srcProperty.EnumProperty.Value);
            }

            destProperty.SetPropertyName(srcProperty.PropertyName);
            destProperty.SetCsType(srcProperty.CsType);
            destProperty.SetCsNullable(srcProperty.CsNullable);
            destProperty.SetCsSize(srcProperty.CsSize);

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

            destProperty.SetPropertyName(srcProperty.PropertyName);

            if (!options.UpdateConstraintNames && srcProperty.RelationPart != null)
                destProperty.RelationPart.Relation.ConstraintName = srcProperty.RelationPart.Relation.ConstraintName;
        }
    }
}
