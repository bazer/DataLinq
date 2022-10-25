using DataLinq.Attributes;
using DataLinq.Metadata;
using System;
using System.Linq;

namespace DataLinq.Tools
{
    public struct MetadataTransformerOptions
    {
        public bool RemoveInterfacePrefix { get; set; }

        public MetadataTransformerOptions(bool removeInterfacePrefix = true)
        {
            RemoveInterfacePrefix = removeInterfacePrefix;
        }
    }

    internal class MetadataTransformer
    {
        private readonly Action<string> log;
        private readonly MetadataTransformerOptions options;

        public MetadataTransformer(Action<string> log, MetadataTransformerOptions options)
        {
            this.log = log;
            this.options = options;
        }

        public void Transform(DatabaseMetadata srcMetadata, DatabaseMetadata destMetadata)
        {
            foreach (var srcTable in srcMetadata.Tables)
            {
                var destTable = destMetadata.Tables.FirstOrDefault(x => x.DbName == srcTable.DbName);

                if (destTable == null)
                {
                    log($"Couldn't find table with name '{srcTable.DbName}' in {nameof(destMetadata)}");
                    continue;
                }

                log($"Transforming model '{srcTable.DbName}'");
                var modelCsTypeName = srcTable.Model.CsTypeName;

                if (options.RemoveInterfacePrefix)
                {
                    if (modelCsTypeName.StartsWith("I"))
                        modelCsTypeName = modelCsTypeName.Substring(1);
                }

                if (destTable.Model.CsTypeName != modelCsTypeName)
                {
                    destTable.Model.CsTypeName = modelCsTypeName;

                    foreach (var enumProp in destTable.Model.Properties.OfType<EnumProperty>())
                    {
                        enumProp.CsTypeName = modelCsTypeName + enumProp.CsName;
                    }
                }

                destTable.Model.Interfaces = new Type[] { srcTable.Model.CsType };
                destTable.Model.CsDatabasePropertyName = srcTable.Model.CsDatabasePropertyName;

                foreach (var srcProperty in srcTable.Model.ValueProperties)
                {
                    var destProperty = destTable.Model.ValueProperties.FirstOrDefault(x => x.Column?.DbName == srcProperty.Column?.DbName);

                    if (destProperty == null)
                    {
                        log($"Couldn't find property with name '{srcProperty.CsName}' in {destTable.DbName}");
                        continue;
                    }

                    destProperty.CsName = srcProperty.CsName;

                    if (destProperty is EnumProperty destEnumProp)
                    {
                        destEnumProp.CsTypeName = modelCsTypeName + destEnumProp.CsName;
                    }
                }

                foreach (var srcProperty in srcTable.Model.RelationProperties)
                {
                    var destProperty = destTable.Model.RelationProperties.FirstOrDefault(x =>
                        srcProperty.Attributes.OfType<RelationAttribute>().Any(y => x.RelationPart.GetOtherSide().Column.Table.DbName == y.Table) &&
                        srcProperty.Attributes.OfType<RelationAttribute>().Any(y => x.RelationPart.GetOtherSide().Column.DbName == y.Column));

                    if (destProperty == null)
                    {
                        log($"Couldn't find property with name '{srcProperty.CsName}' in {destTable.DbName}");
                        continue;
                    }

                    destProperty.CsName = srcProperty.CsName;
                }
            }
        }
    }
}
