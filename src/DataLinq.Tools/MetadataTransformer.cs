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

                foreach (var srcColumn in srcTable.Columns)
                {
                    var destColumn = destTable.Columns.FirstOrDefault(x => x.DbName == srcColumn.DbName);

                    if (destColumn == null)
                    {
                        log($"Couldn't find property with name '{srcColumn.CsName}' in {destTable.DbName}");
                        continue;
                    }

                    destColumn.CsName = srcColumn.CsName;
                    
                    foreach (var destRelation in destColumn.RelationParts)
                    {
                        //var destRelation = destColumn.RelationParts.FirstOrDefault(x => x.Relation.ConstraintName == srcRelation.Relation.ConstraintName);



                        if (destRelation == null)
                        {
                            log($"Couldn't find relation with name '{destRelation.Relation.ConstraintName}' in {destColumn.CsName}");
                            continue;
                        }

                        //destRelation.CsName = srcRelation.CsName;
                    }
                }
            }
        }
    }
}
