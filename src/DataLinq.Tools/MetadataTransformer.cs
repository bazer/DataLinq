using DataLinq.Attributes;
using DataLinq.Extensions;
using DataLinq.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                var csName = srcTable.Model.CsTypeName;

                if (options.RemoveInterfacePrefix)
                {
                    if (csName.StartsWith("I"))
                        csName = csName.Substring(1);
                }

                destTable.Model.CsTypeName = csName;
                destTable.Model.Interfaces = new Type[] { srcTable.Model.CsType };

                destTable.Model.CsDatabasePropertyName = srcTable.Model.CsDatabasePropertyName;

                foreach (var srcProperty in srcTable.Model.ValueProperties)
                {
                    var destProperty = destTable.Model.ValueProperties.FirstOrDefault(x => x.Column?.DbName == srcProperty.Column?.DbName);
                    //var destColumn = destTable.Columns.FirstOrDefault(x => x.DbName == srcProperty.DbName);

                    if (destProperty == null)
                    {
                        log($"Couldn't find property with name '{srcProperty.CsName}' in {destTable.DbName}");
                        continue;
                    }

                    destProperty.CsName = srcProperty.CsName;
                    
                    //foreach (var destRelation in destColumn.RelationParts)
                    //{
                    //    //var destRelation = destColumn.RelationParts.FirstOrDefault(x => x.Relation.ConstraintName == srcRelation.Relation.ConstraintName);



                    //    if (destRelation == null)
                    //    {
                    //        log($"Couldn't find relation with name '{destRelation.Relation.ConstraintName}' in {destColumn.CsName}");
                    //        continue;
                    //    }

                    //    //destRelation.CsName = srcRelation.CsName;
                    //}
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

                    //foreach (var destRelation in destColumn.RelationParts)
                    //{
                    //    //var destRelation = destColumn.RelationParts.FirstOrDefault(x => x.Relation.ConstraintName == srcRelation.Relation.ConstraintName);



                    //    if (destRelation == null)
                    //    {
                    //        log($"Couldn't find relation with name '{destRelation.Relation.ConstraintName}' in {destColumn.CsName}");
                    //        continue;
                    //    }

                    //    //destRelation.CsName = srcRelation.CsName;
                    //}
                }
            }
        }
    }
}
